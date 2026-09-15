using AiProject.Company.Contracts;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.WorkHours;
using AiProject.Console.CompanyClient;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    public List<ReportingDestination> ReportingDestinations { get; private set; } = [];
    public string SelectedReportingDestinationId { get; set; } = "";
    public string NewDestinationName { get; set; } = "";
    public string NewDestinationUrl { get; set; } = "";
    public string DestinationHint { get; private set; } = "";
    public bool DestinationTestBusy { get; private set; }

    public IReadOnlyList<AssignmentDto> CompanyAssignments { get; private set; } = [];
    public string CompanyUploadHint { get; private set; } = "";
    public bool CompanyUploadBusy { get; private set; }

    public ReportingDestination? SelectedReportingDestination =>
        ReportingDestinations.FirstOrDefault(d => d.Id == SelectedReportingDestinationId)
        ?? ReportingDestinations.FirstOrDefault(d => d.Enabled)
        ?? ReportingDestinations.FirstOrDefault();

    public IReadOnlyList<ReportingDestination> EnabledReportingDestinations =>
        ReportingDestinations.Where(d => d.Enabled).ToList();

    public string SendToCompanyLabel
    {
        get
        {
            var dest = SelectedReportingDestination;
            if (dest is null || !dest.Enabled)
                return "送到公司";
            var name = string.IsNullOrWhiteSpace(dest.DisplayName) ? "這家公司" : dest.DisplayName;
            return $"送到〔{name}〕";
        }
    }

    /// <summary>過渡相容：設定頁仍可顯示主要網址。</summary>
    public string CompanyBaseUrl
    {
        get => SelectedReportingDestination?.BaseUrl ?? "";
        set
        {
            // 舊綁定：若只有一筆則改 URL；否則忽略（請用目的地清單）
            if (ReportingDestinations.Count == 1)
                ReportingDestinations[0].BaseUrl = (value ?? "").Trim().TrimEnd('/');
        }
    }

    public IReadOnlyList<DateOnly> StatusChartDays
    {
        get
        {
            var (start, end) = WorkHoursAggregator.Range(WorkHoursView is WorkHoursView.Week or WorkHoursView.Month ? WorkHoursView : WorkHoursView.Week, WorkHoursAnchor);
            var days = new List<DateOnly>();
            for (var d = start; d <= end; d = d.AddDays(1))
                days.Add(d);
            return days;
        }
    }

    public int ChartIntensity(string projectKey, DateOnly date) => _statusChart.Intensity(projectKey, date);

    public void CycleStatusChart(string projectKey, DateOnly date)
    {
        _statusChart.Cycle(projectKey, date);
        Notify();
    }

    public void LoadReportingDestinationsFromStore()
    {
        ReportingDestinations = ConsoleSettingsStore.GetReportingDestinations().Select(CloneDest).ToList();
        SelectedReportingDestinationId = ConsoleSettingsStore.GetSelectedReportingDestinationId()
            ?? ReportingDestinations.FirstOrDefault(d => d.Enabled)?.Id
            ?? ReportingDestinations.FirstOrDefault()?.Id
            ?? "";
        Notify();
    }

    public void PersistReportingDestinations()
    {
        ConsoleSettingsStore.SetReportingDestinations(ReportingDestinations, SelectedReportingDestinationId);
    }

    public void AddReportingDestinationDraft()
    {
        var url = (NewDestinationUrl ?? "").Trim().TrimEnd('/');
        var name = (NewDestinationName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            DestinationHint = "請填 Base URL，例如 https://company.example.com。";
            Notify();
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            DestinationHint = "網址要是 http 或 https 開頭。";
            Notify();
            return;
        }
        if (ReportingDestinations.Any(d => string.Equals(d.BaseUrl, url, StringComparison.OrdinalIgnoreCase)))
        {
            DestinationHint = "這個網址已在清單裡。";
            Notify();
            return;
        }
        var dest = new ReportingDestination
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(name) ? uri.Host : name,
            BaseUrl = url,
            ContractVersion = "1",
            Enabled = false,
        };
        ReportingDestinations.Add(dest);
        SelectedReportingDestinationId = dest.Id;
        NewDestinationName = "";
        NewDestinationUrl = "";
        DestinationHint = "已加入。請先「測試連線」，通過後再啟用。";
        Notify();
    }

    public void RemoveReportingDestination(string id)
    {
        ReportingDestinations.RemoveAll(d => d.Id == id);
        if (SelectedReportingDestinationId == id)
            SelectedReportingDestinationId = ReportingDestinations.FirstOrDefault()?.Id ?? "";
        DestinationHint = "";
        Notify();
    }

    public void SetSelectedReportingDestination(string id)
    {
        SelectedReportingDestinationId = id ?? "";
        Notify();
        _ = RefreshCompanyAssignmentsAsync();
    }

    public void EnableReportingDestination(string id)
    {
        var dest = ReportingDestinations.FirstOrDefault(d => d.Id == id);
        if (dest is null)
            return;
        if (dest.LastTestOk != true)
        {
            DestinationHint = "請先測試連線通過，才能啟用這家公司。";
            Notify();
            return;
        }
        dest.Enabled = true;
        DestinationHint = $"已啟用〔{dest.DisplayName}〕。現在可以「送到〔{dest.DisplayName}〕」。";
        Notify();
    }

    public void DisableReportingDestination(string id)
    {
        var dest = ReportingDestinations.FirstOrDefault(d => d.Id == id);
        if (dest is null)
            return;
        dest.Enabled = false;
        DestinationHint = $"已停用〔{dest.DisplayName}〕。本機工時不受影響。";
        Notify();
    }

    public async Task TestReportingDestinationAsync(string id)
    {
        if (DestinationTestBusy || _company is null)
            return;
        var dest = ReportingDestinations.FirstOrDefault(d => d.Id == id);
        if (dest is null)
            return;
        DestinationTestBusy = true;
        DestinationHint = $"正在測試〔{dest.DisplayName}〕…";
        Notify();
        try
        {
            var token = await GithubTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                dest.LastTestOk = false;
                dest.LastTestMessage = "請先用 GitHub 登入後再測試連線。";
                dest.LastTestedAt = DateTimeOffset.UtcNow;
                dest.Enabled = false;
                DestinationHint = dest.LastTestMessage;
                return;
            }
            var me = await _company.MeAsync(dest.BaseUrl, token);
            dest.LastTestedAt = DateTimeOffset.UtcNow;
            if (me.Matched)
            {
                dest.LastTestOk = true;
                dest.LastTestMessage = string.IsNullOrWhiteSpace(me.Message) ? "已對到人員，可以申報工時。" : me.Message;
                DestinationHint = $"〔{dest.DisplayName}〕測試通過。可以按「啟用這家公司」。";
            }
            else
            {
                dest.LastTestOk = false;
                dest.Enabled = false;
                dest.LastTestMessage = string.IsNullOrWhiteSpace(me.Message)
                    ? "這家公司還沒有你的人員檔，請找對方人資。"
                    : me.Message;
                DestinationHint = dest.LastTestMessage;
            }
        }
        catch (CompanyPlatformException ex)
        {
            dest.LastTestOk = false;
            dest.Enabled = false;
            dest.LastTestedAt = DateTimeOffset.UtcNow;
            dest.LastTestMessage = HumanizeConnectionFailure(ex.Message);
            DestinationHint = dest.LastTestMessage;
        }
        catch (Exception ex)
        {
            dest.LastTestOk = false;
            dest.Enabled = false;
            dest.LastTestedAt = DateTimeOffset.UtcNow;
            dest.LastTestMessage = HumanizeConnectionFailure(ex.Message);
            DestinationHint = dest.LastTestMessage;
        }
        finally
        {
            DestinationTestBusy = false;
            Notify();
        }
    }

    static string HumanizeConnectionFailure(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "連線測試失敗，工作區沒有改動。";
        if (raw.Contains("404", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Name or service", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("連線被拒", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "這個網址沒有公開回報契約（或主機打不開）。連線測試失敗，工作區沒有改動。";
        if (raw.Contains("403", StringComparison.OrdinalIgnoreCase) || raw.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            return "這家公司還沒有你的人員檔，請找對方人資。";
        return raw;
    }

    public async Task RefreshCompanyAssignmentsAsync()
    {
        var dest = SelectedReportingDestination;
        if (_company is null || dest is null || string.IsNullOrWhiteSpace(dest.BaseUrl) || !dest.Enabled)
        {
            CompanyAssignments = [];
            Notify();
            return;
        }
        try
        {
            var token = await GithubTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                CompanyAssignments = [];
                Notify();
                return;
            }
            CompanyAssignments = await _company.AssignmentsAsync(dest.BaseUrl, token);
        }
        catch (Exception ex)
        {
            CompanyUploadHint = "讀取公司派工失敗：" + ex.Message;
        }
        Notify();
    }

    public async Task SendTimesheetToCompanyAsync()
    {
        if (CompanyUploadBusy)
            return;
        var dest = SelectedReportingDestination;
        if (dest is null || string.IsNullOrWhiteSpace(dest.BaseUrl))
        {
            _native.Info("尚未設定申報目的地", "請在設定加入公司工作區網址，測試連線並啟用。本機 work-hours.json 仍在。");
            return;
        }
        if (!dest.Enabled)
        {
            _native.Info("這家公司尚未啟用", "請先「測試連線」通過後再啟用，才能送出。本機 work-hours.json 仍在。");
            return;
        }
        if (_company is null)
        {
            _native.Error("送到公司失敗", "控制台沒有公司平台用戶端。");
            return;
        }
        var token = await GithubTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _native.Info("請先登入 GitHub", "送到公司要用你的 GitHub 帳號對人。");
            return;
        }
        CompanyUploadBusy = true;
        CompanyUploadHint = "";
        Notify();
        try
        {
            try
            {
                CompanyAssignments = await _company.AssignmentsAsync(dest.BaseUrl, token);
            }
            catch (Exception ex)
            {
                CompanyUploadHint = "讀取公司派工失敗：" + ex.Message;
                return;
            }
            var map = ConsoleSettingsStore.GetCompanyProjectMap();
            var sessions = _workHours.VisibleSessions;
            var (start, end) = WorkHoursAggregator.Range(WorkHoursView, WorkHoursAnchor);
            var now = DateTimeOffset.Now;
            var sent = 0;
            var unmatched = new List<string>();
            foreach (var session in sessions)
            {
                if (!session.HasProject)
                    continue;
                var date = DateOnly.FromDateTime(session.StartedAt.LocalDateTime);
                if (date < start || date > end)
                    continue;
                var hours = (decimal)session.Duration(now).TotalHours;
                if (hours <= 0)
                    continue;
                var projectId = CompanyProjectMatcher.Resolve(session.GithubSlug, session.ProjectName, session.ProjectKey, CompanyAssignments, map);
                if (projectId is null)
                {
                    unmatched.Add(session.DisplayName);
                    continue;
                }
                ConsoleSettingsStore.SetCompanyProjectId(session.ProjectKey, projectId.Value);
                var chart = _statusChart.Snapshot()
                    .Where(c => string.Equals(c.ProjectKey, session.ProjectKey, StringComparison.OrdinalIgnoreCase))
                    .Select(c => new StatusChartCellDto { ProjectId = projectId.Value, Date = c.Date, Intensity = c.Intensity, Note = c.Note })
                    .ToList();
                var request = new TimesheetUploadRequest
                {
                    LocalSlotId = session.Id,
                    ProjectId = projectId.Value,
                    WorkDate = date,
                    Hours = decimal.Round(hours, 2),
                    Chart = chart,
                };
                await _company.UploadAsync(dest.BaseUrl, request, token);
                sent++;
            }
            var label = string.IsNullOrWhiteSpace(dest.DisplayName) ? "公司" : dest.DisplayName;
            if (sent == 0 && unmatched.Count == 0)
                CompanyUploadHint = "這個期間沒有可送的本機時段。";
            else if (unmatched.Count > 0)
                CompanyUploadHint = (sent == 0 ? "" : $"已送到〔{label}〕 {sent} 筆。") + "本機專案對不到公司派工：" + string.Join("、", unmatched.Distinct()) + "。請先有派工，或確認 GitHub 倉與公司專案一致。本機 work-hours.json 仍在。";
            else
                CompanyUploadHint = $"已送到〔{label}〕 {sent} 筆，狀態為待 PM 確認。本機 work-hours.json 仍在。";
        }
        catch (CompanyPlatformException ex)
        {
            CompanyUploadHint = ex.Message + " 可以重試。本機檔仍在。";
        }
        catch (Exception ex)
        {
            CompanyUploadHint = "送到公司失敗：" + ex.Message + " 可以重試。本機 work-hours.json 仍在。";
        }
        finally
        {
            CompanyUploadBusy = false;
            Notify();
        }
    }

    async Task<string> GithubTokenAsync()
    {
        var (code, output, _) = await GhCli.RunCaptureAsync(["auth", "token"], timeoutMs: 15_000);
        return code == 0 ? output.Trim() : "";
    }

    static ReportingDestination CloneDest(ReportingDestination d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        BaseUrl = d.BaseUrl,
        ContractVersion = d.ContractVersion,
        Enabled = d.Enabled,
        LastTestOk = d.LastTestOk,
        LastTestMessage = d.LastTestMessage,
        LastTestedAt = d.LastTestedAt,
    };
}

using AiProject.Company.Contracts;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.WorkHours;
using AiProject.Console.CompanyClient;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    public IReadOnlyList<AssignmentDto> CompanyAssignments { get; private set; } = [];
    public string CompanyUploadHint { get; private set; } = "";
    public bool CompanyUploadBusy { get; private set; }

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

    public async Task RefreshCompanyAssignmentsAsync()
    {
        if (_company is null || string.IsNullOrWhiteSpace(CompanyBaseUrl))
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
            CompanyAssignments = await _company.AssignmentsAsync(token);
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
        if (string.IsNullOrWhiteSpace(CompanyBaseUrl))
        {
            _native.Info("尚未設定公司平台", "請在設定填寫公司平台網址。本機 work-hours.json 仍在。");
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
                    CompanyAssignments = await _company.AssignmentsAsync(token);
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
                    await _company.UploadAsync(request, token);
                    sent++;
                }
                if (sent == 0 && unmatched.Count == 0)
                    CompanyUploadHint = "這個期間沒有可送的本機時段。";
                else if (unmatched.Count > 0)
                    CompanyUploadHint = (sent == 0 ? "" : $"已送到公司 {sent} 筆。") + "本機專案對不到公司派工：" + string.Join("、", unmatched.Distinct()) + "。請先有派工，或確認 GitHub 倉與公司專案一致。本機 work-hours.json 仍在。";
                else
                    CompanyUploadHint = $"已送到公司 {sent} 筆，狀態為待 PM 確認。本機 work-hours.json 仍在。";
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
}

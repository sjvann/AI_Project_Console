using System.Text.Json.Serialization;
using AiProject.Shared.Contracts;

namespace AiProject.Company.Contracts;

public static class CompanyApiVersions
{
    public const string Current = ReportingContract.CurrentVersion;
    public const string Header = ReportingContract.ApiVersionHeader;
}

public sealed class TimesheetUploadRequest
{
    public string LocalSlotId { get; set; } = "";
    /// <summary>內部專案 Guid；可與 ProjectCode／Repos 擇一。</summary>
    public Guid? ProjectId { get; set; }
    /// <summary>公司對外專案碼。</summary>
    public string? ProjectCode { get; set; }
    /// <summary>GitHub owner/repo 列表（對應工作區專案已綁倉）。</summary>
    public List<string> Repos { get; set; } = [];
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public List<int> IssueNumbers { get; set; } = [];
    /// <summary>CHAOSS 貢獻類型，如 code／issue／docs。</summary>
    public List<string> ContributionTypes { get; set; } = [];
    public List<StatusChartCellDto> Chart { get; set; } = [];
    public bool IsCorrection { get; set; }
    public string? CorrectsLocalSlotId { get; set; }
    /// <summary>外包窗口代送時填被代送工程師的 GitHub login。</summary>
    public string? OnBehalfOfGitHubLogin { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    public bool HasForbiddenFields() =>
        ReportingContract.HasForbiddenExtensionKeys(Extra?.Keys);
}

public sealed class StatusChartCellDto
{
    public Guid ProjectId { get; set; }
    public DateOnly Date { get; set; }
    public int Intensity { get; set; }
    public string? Note { get; set; }
}

public sealed class TimesheetUploadResponse
{
    public Guid TimesheetId { get; set; }
    public string Status { get; set; } = "pending_pm";
    public string Message { get; set; } = "待 PM 確認";
}

public sealed class AssignmentDto
{
    public Guid AssignmentId { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public decimal HoursPerWeek { get; set; }
    public string Role { get; set; } = "";
    public string SyncState { get; set; } = "";
    public List<string> Repos { get; set; } = [];
}

public sealed class PayslipDto
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string PeriodStatus { get; set; } = "";
    public decimal PayableTotal { get; set; }
    public List<PayslipLineDto> Lines { get; set; } = [];
}

public sealed class PayslipLineDto
{
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Hours { get; set; }
    public string Note { get; set; } = "";
    public Guid? ProjectId { get; set; }
}

public sealed class MeDto
{
    public Guid? PersonId { get; set; }
    public Guid TenantId { get; set; }
    public string GitHubLogin { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    /// <summary>是否已對到本租戶人員／邀請（可申報）。</summary>
    public bool Matched { get; set; }
    public string Message { get; set; } = "";
}

public sealed class ApiError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

using System.Text.Json.Serialization;

namespace AiProject.Company.Contracts;

public static class CompanyApiVersions
{
    public const string Current = "1";
    public const string Header = "X-Company-Api-Version";
}

public sealed class TimesheetUploadRequest
{
    public string LocalSlotId { get; set; } = "";
    public Guid ProjectId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public List<int> IssueNumbers { get; set; } = [];
    public List<StatusChartCellDto> Chart { get; set; } = [];
    public bool IsCorrection { get; set; }
    public string? CorrectsLocalSlotId { get; set; }
    /// <summary>外包窗口代送時填被代送工程師的 GitHub login。</summary>
    public string? OnBehalfOfGitHubLogin { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    public bool HasForbiddenFields()
    {
        if (Extra is null || Extra.Count == 0)
            return false;
        foreach (var key in Extra.Keys)
        {
            if (key.Contains("path", StringComparison.OrdinalIgnoreCase)
                || key.Contains("blob", StringComparison.OrdinalIgnoreCase)
                || key.Contains("source", StringComparison.OrdinalIgnoreCase)
                || key.Contains("file", StringComparison.OrdinalIgnoreCase)
                || key.Contains("code", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
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

public sealed class ApiError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

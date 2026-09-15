namespace AiProject.Company.Domain;

public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public readonly record struct Outcome
{
    public bool Ok { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }

    public static Outcome Success() => new() { Ok = true, Code = "", Message = "" };

    public static Outcome Fail(string code, string message) =>
        new() { Ok = false, Code = code, Message = message };

    public void ThrowIfFailed()
    {
        if (!Ok)
            throw new DomainException(Code, Message);
    }
}

public readonly record struct Outcome<T>
{
    public bool Ok { get; init; }
    public T? Value { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }

    public static Outcome<T> Success(T value) =>
        new() { Ok = true, Value = value, Code = "", Message = "" };

    public static Outcome<T> Fail(string code, string message) =>
        new() { Ok = false, Value = default, Code = code, Message = message };

    public T Unwrap()
    {
        if (!Ok || Value is null)
            throw new DomainException(Code, Message);
        return Value;
    }
}

public static class ErrorCodes
{
    public const string NotInvited = "not_invited";
    public const string Forbidden = "forbidden";
    public const string Unauthenticated = "unauthenticated";
    public const string BadCredentials = "bad_credentials";
    public const string GithubBound = "github_bound";
    public const string PersonInactive = "person_inactive";
    public const string ProjectClosed = "project_closed";
    public const string VendorNotAuthorized = "vendor_not_authorized";
    public const string OverloadRequiresReason = "overload_requires_reason";
    public const string TimesheetApprovedImmutable = "timesheet_approved_immutable";
    public const string PayrollLocked = "payroll_locked";
    public const string SoftDeleteBlocked = "soft_delete_blocked";
    public const string MissingReason = "missing_reason";
    public const string RateHidden = "rate_hidden";
    public const string Required = "required";
    public const string Conflict = "conflict";
    public const string NotFound = "not_found";
    public const string UnmatchedPerson = "unmatched_person";
    public const string BonusNotPayable = "bonus_not_payable";
    public const string InvalidState = "invalid_state";
    public const string UsernameTaken = "username_taken";
    public const string LastOwner = "last_owner";
    public const string ClientNotActive = "client_not_active";
    public const string ClientCannotEstablish = "client_cannot_establish";
    public const string ProjectUnresolved = "project_unresolved";
}

public static class Messages
{
    public static string NotInvited => "這個 GitHub 帳號尚未受邀，請聯絡公司管理員。";
    public static string Forbidden => "你沒有權限做這件事。";
    public static string Unauthenticated => "請先登入。";
    public static string BadCredentials => "帳號或密碼不對。";
    public static string GithubBound => "這個 GitHub 帳號已經綁到另一位在職人員。";
    public static string PersonInactive => "停用或黑名單的人員不能新派工。";
    public static string ProjectClosed => "結案專案不能新派工。";
    public static string VendorNotAuthorized => "這位外包不在合約名單裡。";
    public static string OverloadRequiresReason => "超載派工必須填原因。";
    public static string TimesheetApprovedImmutable => "已核准的時段不能覆蓋，請新增更正時段。";
    public static string PayrollLocked => "這個薪資週期已鎖定，只能開更正週期。";
    public static string SoftDeleteBlocked => "已有工時或派工，不能硬刪，請改為停用。";
    public static string MissingReason => "請填寫原因。";
    public static string RateHidden => "你不能看這筆費率或月薪。";
    public static string UsernameTaken => "這個登入帳號已被使用。";
    public static string LastOwner => "不能停用最後一位公司管理員。";
    public static string CompanyAccountInstead => "公司管理人員請開公司帳戶，不要用 GitHub 邀請。";
    public static string GitHubInviteInstead => "系統分析師與工程師請走 GitHub 邀請（線上招募／控制台），不要開公司後台帳戶。";
    public static string WeakPassword => "密碼至少 8 個字。";
    public static string Required(string field) => $"請填{field}。";
    public static string NotFound(string thing) => $"找不到{thing}。";
    public static string UnmatchedPerson => "這次上傳還沒對到人員檔，請找人資綁定 GitHub 帳號。";
    public static string BonusNotPayable => "專案獎金還沒標記可發，不會進入實發。";
    public static string InvalidState => "目前狀態不能做這個動作。";
    public static string ClientNotActive => "潛在或未成交的客戶不能派工，也不列入戰情室進行中。";
    public static string ClientCannotEstablish => "要先進入議約或已是合約客戶，才能成立專案。";
    public static string ProjectUnresolved => "請提供專案 Guid、專案碼或已綁定的 GitHub 倉（owner/repo）至少一種。";
}

public static class Currencies
{
    public const string Other = "__other";

    public static readonly string[] Common =
    [
        "TWD", "USD", "EUR", "JPY", "CNY", "HKD", "SGD", "GBP", "AUD", "KRW",
    ];

    public static string Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "TWD" : code.Trim().ToUpperInvariant();
}

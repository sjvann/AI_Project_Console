using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>SaaS 或 SelfHosted（同一映像，見 AD-12）。</summary>
    public string Mode { get; set; } = HostingModes.SelfHosted;

    public string NormalizedMode => HostingModes.Normalize(Mode);

    public bool IsSaaS => HostingModes.IsSaaS(Mode);

    public string OperatorLabel => IsSaaS ? "這是我們代管的租戶" : "這是本公司主機（自架）";
}

using AiProject.Company.Contracts;
using AiProject.Shared.Contracts;

namespace AiProject.Shared.ContractTests;

public class ReportingContractTests
{
    [Theory]
    [InlineData("sourcePath")]
    [InlineData("blob")]
    [InlineData("fileName")]
    [InlineData("source")]
    [InlineData("codeSnippet")]
    public void Forbidden_extension_keys_are_rejected(string key)
    {
        Assert.True(ReportingContract.IsForbiddenExtensionKey(key));
        Assert.True(ReportingContract.HasForbiddenExtensionKeys([key, "hours"]));
    }

    [Fact]
    public void Safe_extension_keys_pass()
    {
        Assert.False(ReportingContract.HasForbiddenExtensionKeys(["contributionTypes", "timeZone", "reportedAt"]));
    }

    [Fact]
    public void Upload_request_rejects_source_path_extra()
    {
        var req = new TimesheetUploadRequest
        {
            LocalSlotId = "s",
            Extra = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["sourcePath"] = System.Text.Json.JsonDocument.Parse("\"C:\\\\repo\"").RootElement.Clone(),
            },
        };
        Assert.True(req.HasForbiddenFields());
    }

    [Fact]
    public void Version_header_matches_company_contracts()
    {
        Assert.Equal(ReportingContract.ApiVersionHeader, CompanyApiVersions.Header);
        Assert.Equal(ReportingContract.CurrentVersion, CompanyApiVersions.Current);
    }
}

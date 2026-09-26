using System.Text.Json;
using AiProject.Company.Infrastructure;

namespace AiProject.Company.Web.Tests;

public class WorkspaceIntakeReaderTests
{
    [Fact]
    public void Summarize_CountsIssuedAndPendingFromIssuesNotStage()
    {
        var json = """
            {
              "version": "1",
              "intakes": [
                { "id": "ISS-1", "stage": "doing", "items": [{ "issueNumber": 9 }] },
                { "id": "ISS-2", "stage": "accepted", "acceptedAt": "2026-09-01T00:00:00Z", "items": [{ "issueNumber": 10 }] },
                { "id": "ISS-3", "stage": "issued", "hold": "recalled", "items": [{ "issueNumber": 11 }] },
                { "id": "ISS-4", "stage": "draft", "items": [] }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var summary = WorkspaceIntakeReader.Summarize(doc.RootElement);
        Assert.Equal(2, summary.Issued);
        Assert.Equal(1, summary.PendingAcceptance);
    }
}

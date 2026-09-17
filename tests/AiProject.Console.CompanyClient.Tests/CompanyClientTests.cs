using System.Net;
using System.Net.Http.Json;
using AiProject.Company.Contracts;
using AiProject.Console.CompanyClient;

namespace AiProject.Console.CompanyClient.Tests;

sealed class StubHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }
}

public class CompanyClientTests
{
    [Fact]
    public async Task Upload_retries_on_server_error_then_succeeds()
    {
        var n = 0;
        var handler = new StubHandler
        {
            Respond = _ =>
            {
                n++;
                if (n < 3)
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new TimesheetUploadResponse { TimesheetId = Guid.NewGuid(), Status = "pending_pm", Message = "待 PM 確認" }),
                };
            },
        };
        var client = new CompanyPlatformClient(new HttpClient(handler) { BaseAddress = new Uri("https://company.test/") });
        var result = await client.UploadAsync("https://company.test/", new TimesheetUploadRequest { LocalSlotId = "old-client-slot", Hours = 2, WorkDate = new DateOnly(2026, 1, 1), ProjectId = Guid.NewGuid() }, "tok");
        Assert.Equal("pending_pm", result.Status);
        Assert.Equal(3, n);
        Assert.Contains(handler.Requests, r => r.Headers.Contains(CompanyApiVersions.Header));
    }

    [Fact]
    public async Task Me_uses_absolute_base_url()
    {
        var handler = new StubHandler
        {
            Respond = req =>
            {
                Assert.Equal("https://acme.test/api/v1/me", req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new MeDto { Matched = true, Message = "已對到人員，可以申報工時。", GitHubLogin = "alice" }),
                };
            },
        };
        var client = new CompanyPlatformClient(new HttpClient(handler));
        var me = await client.MeAsync("https://acme.test", "tok");
        Assert.True(me.Matched);
    }

    [Fact]
    public async Task Old_payload_without_correction_flag_still_deserializes()
    {
        var json = """{"localSlotId":"s1","projectId":"11111111-1111-1111-1111-111111111111","workDate":"2026-09-01","hours":8,"issueNumbers":[12]}""";
        var dto = System.Text.Json.JsonSerializer.Deserialize<TimesheetUploadRequest>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal("s1", dto!.LocalSlotId);
        Assert.False(dto.IsCorrection);
        Assert.Equal(8, dto.Hours);
        Assert.Equal(12, dto.IssueNumbers[0]);
    }

    [Fact]
    public void Matches_github_repo_on_assignment()
    {
        var projectId = Guid.NewGuid();
        var assignments = new List<AssignmentDto>
        {
            new()
            {
                ProjectId = projectId,
                ProjectName = "控制台",
                Repos = ["acme/app"],
            },
        };
        var id = CompanyProjectMatcher.Resolve("acme/app", "AI_Project_Console", "gh:acme/app", assignments, new Dictionary<string, Guid>());
        Assert.Equal(projectId, id);
    }

    [Fact]
    public void Local_map_wins_over_name()
    {
        var mapped = Guid.NewGuid();
        var assignments = new List<AssignmentDto>
        {
            new() { ProjectId = Guid.NewGuid(), ProjectName = "控制台", Repos = ["acme/app"] },
        };
        var id = CompanyProjectMatcher.Resolve("acme/app", "控制台", "gh:acme/app", assignments, new Dictionary<string, Guid> { ["gh:acme/app"] = mapped });
        Assert.Equal(mapped, id);
    }

    [Fact]
    public void Match_local_name_uses_repo_then_map()
    {
        var projectId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var assignments = new List<AssignmentDto>
        {
            new() { ProjectId = projectId, ProjectName = "控制台", Repos = ["acme/app"] },
            new() { ProjectId = otherId, ProjectName = "別專案", Repos = ["acme/other"] },
        };
        var locals = new List<(string Key, string Name, string GithubSlug)>
        {
            ("gh:acme/app", "AI_Project_Console", "acme/app"),
        };
        var name = CompanyProjectMatcher.MatchLocalName(assignments[0], locals, assignments, new Dictionary<string, Guid>());
        Assert.Equal("AI_Project_Console", name);
        Assert.Null(CompanyProjectMatcher.MatchLocalName(assignments[1], locals, assignments, new Dictionary<string, Guid>()));
    }
}

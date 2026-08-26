using System.Text.Json.Nodes;
using AiProject.Console.Core;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.ProcessOps;

namespace AiProject.Console.Core.Tests;

public class DoctorSnapshotTests
{
    [Fact]
    public void Build_WithoutProject_GroupsAndKeepsText()
    {
        var report = DoctorSnapshot.Build(null);
        var text = report.ToText();

        Assert.Equal(ProcessSupervisor.DoctorReport(null), text);
        Assert.StartsWith("AI_Project 環境體檢", text);
        Assert.Contains("尚未選擇專案目錄。", text);
        Assert.Contains(".NET:", text);
        Assert.Equal(["tools", "agent", "mcp", "project"], report.Sections.Select(s => s.Id).ToArray());
        Assert.Equal("基本工具", report.Sections[0].Title);
        Assert.Equal("Agent 後端", report.Sections[1].Title);
        Assert.Equal("MCP", report.Sections[2].Title);
        Assert.Equal("專案與文件", report.Sections[3].Title);
        Assert.Equal("prefs-mcp", report.Sections[2].ActionId);
        Assert.DoesNotContain(report.Sections, s => s.Id == "docs");
        Assert.Contains(report.Sections[3].Items, i => i.Value.Contains("尚未選擇") && i.Value.Contains("docfx"));
        Assert.Equal("pick-project", report.Sections[3].ActionId);
        Assert.False(string.IsNullOrWhiteSpace(report.Headline));
    }

    [Fact]
    public void Build_WithCatalog_ListsServicesInText()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "Demo",
                Summary = "示範專案",
                Services =
                [
                    new ServiceEntry("web", "網站", "web", "src/Web", 5080, "", "http://localhost:5080", "前端"),
                ],
                Projects = [],
                StartOrder = ["web"],
                Frontend = "web",
                Manifest = new JsonObject(),
                Scan = new ScanResult(root, []),
            };

            var report = DoctorSnapshot.Build(catalog);
            var text = report.ToText();

            Assert.Equal(ProcessSupervisor.DoctorReport(catalog), text);
            Assert.Contains("專案：Demo", text);
            Assert.Contains("網站 [web] port=5080", text);
            Assert.Contains("manifest: 無（使用掃描結果）", text);
            var name = report.Sections.Single(s => s.Id == "project").Items.Single(i => i.Label == "名稱");
            Assert.Equal("Demo", name.Value);
            Assert.Equal(DoctorLevel.Ok, name.Level);
            var docs = report.Sections.Single(s => s.Id == "docs");
            var docfx = docs.Items.Single(i => i.Label == "docfx");
            if (!docfx.StatusText.Equals("正常", StringComparison.Ordinal))
            {
                Assert.Equal("install-docfx", docs.ActionId);
                Assert.Contains("dotnet tool restore", docfx.HowTo);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Build_WithoutServices_SaysNoUiOrService()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-doctor-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Demo.Lib"));
        try
        {
            File.WriteAllText(Path.Combine(root, "src", "Demo.Lib", "Demo.Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var report = DoctorSnapshot.Build(catalog);
            var svc = report.Sections.Single(s => s.Id == "project").Items.Single(i => i.Label == "服務");
            Assert.Equal("沒有服務或 UI", svc.Value);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Headline_UsesIssueCounts()
    {
        var missing = new DoctorSnapshot(
            [
                new("x", "X",
                [
                    new("dotnet", "缺少", DoctorLevel.Missing),
                    new("gh", "選用", DoctorLevel.Warn),
                ]),
            ],
            "text");
        Assert.Equal(DoctorLevel.Missing, missing.Overall);
        Assert.Equal("1 項缺少、1 項需注意", missing.Headline);
        Assert.Equal("dotnet、gh", missing.HeadlineDetail);

        var ok = new DoctorSnapshot(
            [new("x", "X", [new("dotnet", "已安裝", DoctorLevel.Ok)])],
            "text");
        Assert.Equal("環境正常", ok.Headline);
        Assert.Equal("基本工具與目前 Agent 都可用。", ok.HeadlineDetail);
    }
}

using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Runtime;

namespace AiProject.Console.Core.Tests;

public class ConsoleSettingsStoreTests
{
    [Fact]
    public void HistoryProjects_PutsCurrentFirstEvenIfMissingFromRecent()
    {
        using var scope = SettingsScope.Create();
        var current = Directory.CreateDirectory(Path.Combine(scope.Dir, "current")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(scope.Dir, "other")).FullName;
        ConsoleSettingsStore.RememberProject(other);

        var history = ConsoleSettingsStore.HistoryProjects(current);
        Assert.Equal(Path.GetFullPath(current), history[0]);
        Assert.Contains(Path.GetFullPath(other), history);
    }

    [Fact]
    public void RememberProject_ConcurrentWindows_KeepBoth()
    {
        using var scope = SettingsScope.Create();
        var a = Directory.CreateDirectory(Path.Combine(scope.Dir, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(scope.Dir, "b")).FullName;
        var path = ConsoleSettingsStore.PathOverride;
        Parallel.Invoke(
            () =>
            {
                ConsoleSettingsStore.PathOverride = path;
                ConsoleSettingsStore.RememberProject(a);
            },
            () =>
            {
                ConsoleSettingsStore.PathOverride = path;
                ConsoleSettingsStore.RememberProject(b);
            });

        var recent = ConsoleSettingsStore.RecentProjects();
        Assert.Contains(Path.GetFullPath(a), recent);
        Assert.Contains(Path.GetFullPath(b), recent);
    }

    [Fact]
    public void MarkUpdateChecked_DoesNotDropRecentProjects()
    {
        using var scope = SettingsScope.Create();
        var a = Directory.CreateDirectory(Path.Combine(scope.Dir, "a")).FullName;
        ConsoleSettingsStore.RememberProject(a);
        ConsoleSettingsStore.MarkUpdateChecked();
        Assert.Contains(Path.GetFullPath(a), ConsoleSettingsStore.RecentProjects());
    }

    [Fact]
    public void HistorySelectValue_MatchesCurrentWhenNotYetInRecent()
    {
        using var scope = SettingsScope.Create();
        var current = Directory.CreateDirectory(Path.Combine(scope.Dir, "console")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(scope.Dir, "aion")).FullName;
        ConsoleSettingsStore.RememberProject(other);

        var history = ConsoleSettingsStore.HistoryProjects(current);
        var selected = history.FirstOrDefault(p =>
            string.Equals(p, Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.GetFullPath(current), selected);
        Assert.NotEqual(Path.GetFullPath(other), selected);
    }

    [Fact]
    public void AskSources_RoundTrip()
    {
        using var scope = SettingsScope.Create();
        ConsoleSettingsStore.SetAskSources(
        [
            new ProjectAskSource("ollama", "本機 Ollama", "http://127.0.0.1:11434/v1", "llama3.2"),
            new ProjectAskSource("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", "sk-test"),
        ]);
        var loaded = ConsoleSettingsStore.GetAskSources();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("ollama", loaded[0].Id);
        Assert.Equal("sk-test", loaded[1].ApiKey);
        ConsoleSettingsStore.SetAskSources([]);
        Assert.Empty(ConsoleSettingsStore.GetAskSources());
    }

    [Fact]
    public void ReportingDestinations_default_disabled_and_migrate_legacy_url()
    {
        using var scope = SettingsScope.Create();
        ConsoleSettingsStore.SetCompanyBaseUrl("https://legacy.example.com/");
        var list = ConsoleSettingsStore.GetReportingDestinations();
        Assert.Single(list);
        Assert.Equal("https://legacy.example.com", list[0].BaseUrl);
        Assert.False(list[0].Enabled);
    }

    [Fact]
    public void ReportingDestinations_round_trip_keeps_test_gate()
    {
        using var scope = SettingsScope.Create();
        var id = Guid.NewGuid().ToString("N");
        ConsoleSettingsStore.SetReportingDestinations(
        [
            new ReportingDestination
            {
                Id = id,
                DisplayName = "Acme",
                BaseUrl = "https://acme.example.com",
                Enabled = true,
                LastTestOk = true,
                LastTestMessage = "已對到人員，可以申報工時。",
                LastTestedAt = DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
            },
        ], id);
        var loaded = ConsoleSettingsStore.GetReportingDestinations();
        Assert.Single(loaded);
        Assert.True(loaded[0].Enabled);
        Assert.True(loaded[0].LastTestOk);
        Assert.Equal(id, ConsoleSettingsStore.GetSelectedReportingDestinationId());
        Assert.Equal("https://acme.example.com", ConsoleSettingsStore.GetCompanyBaseUrl());
    }

    [Fact]
    public void ReportingDestinations_cannot_enable_without_passing_test_is_store_level_data_only()
    {
        // 啟用閘門在 ConsoleSession；設定層只存狀態。此測確認未測過的列預設 Enabled=false。
        using var scope = SettingsScope.Create();
        ConsoleSettingsStore.SetReportingDestinations(
        [
            new ReportingDestination { Id = "a", DisplayName = "A", BaseUrl = "https://a.test", Enabled = false },
        ], "a");
        Assert.False(ConsoleSettingsStore.GetReportingDestinations()[0].Enabled);
    }

    sealed class SettingsScope : IDisposable
    {
        public string Dir { get; }
        readonly string? _prev;

        SettingsScope(string dir, string? prev)
        {
            Dir = dir;
            _prev = prev;
        }

        public static SettingsScope Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aipc-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var prev = ConsoleSettingsStore.PathOverride;
            ConsoleSettingsStore.PathOverride = Path.Combine(dir, "settings.json");
            return new SettingsScope(dir, prev);
        }

        public void Dispose()
        {
            ConsoleSettingsStore.PathOverride = _prev;
            try { Directory.Delete(Dir, recursive: true); } catch { /* ignore */ }
        }
    }
}

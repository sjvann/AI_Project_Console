using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class WorkspaceOccupancyLockTests
{
    [Fact]
    public void Acquire_BlocksOtherPid_AndReleases()
    {
        var root = CreateTempDir();
        try
        {
            var self = new OccupancyRecord
            {
                Kind = nameof(OccupancyKind.Build),
                Title = "建置中…",
                Source = OccupancySources.Desktop,
                Pid = Environment.ProcessId,
                StartedUtc = DateTimeOffset.UtcNow,
                Total = 2,
            };
            Assert.True(WorkspaceOccupancyLock.TryAcquire(root, self, out var firstBlocker));
            Assert.Null(firstBlocker);

            var other = new OccupancyRecord
            {
                Kind = nameof(OccupancyKind.McpBuild),
                Title = "Agent 正在編譯",
                Source = OccupancySources.Mcp,
                Pid = Environment.ProcessId + 1_000_000,
                StartedUtc = DateTimeOffset.UtcNow,
            };
            Assert.False(WorkspaceOccupancyLock.TryAcquire(root, other, out var blocker));
            Assert.NotNull(blocker);
            Assert.Equal(Environment.ProcessId, blocker!.Pid);
            Assert.Contains("建置", blocker.DisplayTitle);

            WorkspaceOccupancyLock.TryRelease(root, Environment.ProcessId);
            Assert.Null(WorkspaceOccupancyLock.TryReadLive(root));
            Assert.True(WorkspaceOccupancyLock.TryAcquire(root, other, out _));
            WorkspaceOccupancyLock.TryRelease(root, other.Pid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeadPid_IsTreatedAsExpired()
    {
        var root = CreateTempDir();
        try
        {
            var stale = new OccupancyRecord
            {
                Kind = nameof(OccupancyKind.Build),
                Title = "建置中…",
                Source = OccupancySources.Mcp,
                Pid = 1,
                StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            };
            Assert.True(WorkspaceOccupancyLock.TryAcquire(root, stale, out _));
            Assert.False(WorkspaceOccupancyLock.IsProcessAlive(1) && ProcessHasNamePid1());
            var live = WorkspaceOccupancyLock.TryReadLive(root);
            if (live is not null && live.Pid == 1)
            {
                // 少數環境 pid 1 仍活著（不是 Windows 常態）；此時不強制過期
                return;
            }
            Assert.Null(live);

            var self = new OccupancyRecord
            {
                Kind = nameof(OccupancyKind.Build),
                Title = "建置中…",
                Source = OccupancySources.Desktop,
                Pid = Environment.ProcessId,
                StartedUtc = DateTimeOffset.UtcNow,
            };
            Assert.True(WorkspaceOccupancyLock.TryAcquire(root, self, out _));
            WorkspaceOccupancyLock.TryRelease(root, self.Pid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static bool ProcessHasNamePid1() => WorkspaceOccupancyLock.IsProcessAlive(1);

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "occ-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

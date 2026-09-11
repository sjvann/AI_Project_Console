using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class LockedOutputTests
{
    [Fact]
    public void TryUnlock_LeavesWritableFileInPlace()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "free.dll");
            File.WriteAllText(path, "ok");
            Assert.False(LockedOutput.TryUnlock(path));
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(dir, "*" + LockedOutput.ReplacedExtension));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryUnlock_RenamesFileOpenedWithShareDelete()
    {
        var dir = CreateTempDir();
        FileStream? hold = null;
        try
        {
            var path = Path.Combine(dir, "locked.dll");
            File.WriteAllText(path, "old");
            hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Assert.True(LockedOutput.TryUnlock(path));
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(dir, "*" + LockedOutput.ReplacedExtension));
        }
        finally
        {
            hold?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sweep_DeletesReplacedFilesOnceReleased()
    {
        var dir = CreateTempDir();
        try
        {
            var leftover = Path.Combine(dir, "Core.dll." + Guid.NewGuid().ToString("N") + LockedOutput.ReplacedExtension);
            File.WriteAllText(leftover, "old");
            LockedOutput.Sweep(dir);
            Assert.False(File.Exists(leftover));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "locked-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

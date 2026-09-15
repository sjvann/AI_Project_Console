using AiProject.Shared.Update;

namespace AiProject.Console.Core.Tests;

public class ChecksumTests
{
    [Fact]
    public async Task Write_and_verify_sidecar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aiproject-checksum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "AI_Project_Console-0.6.14-win-x64-setup.exe");
            await File.WriteAllTextAsync(file, "payload-for-hash");
            await Checksum.WriteSidecarAsync(file);
            Assert.True(File.Exists(Checksum.SidecarPath(file)));
            Assert.True(await Checksum.TryVerifySidecarAsync(file));
            await File.WriteAllTextAsync(file, "tampered");
            Assert.False(await Checksum.TryVerifySidecarAsync(file));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Missing_sidecar_returns_null()
    {
        var file = Path.Combine(Path.GetTempPath(), "no-side-" + Guid.NewGuid().ToString("N") + ".exe");
        await File.WriteAllTextAsync(file, "x");
        try
        {
            Assert.Null(await Checksum.TryVerifySidecarAsync(file));
        }
        finally
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }
}

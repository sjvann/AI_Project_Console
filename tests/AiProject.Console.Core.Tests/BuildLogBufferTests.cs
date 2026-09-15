using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class BuildLogBufferTests
{
    [Fact]
    public void Append_DropsOldest_BeyondCapacity()
    {
        var buf = new BuildLogBuffer(capacity: 3, flushInterval: TimeSpan.Zero);
        buf.Append("a");
        buf.Append("b");
        buf.Append("c");
        buf.Append("d");
        Assert.Equal(3, buf.Count);
        Assert.True(buf.TryFlush(DateTimeOffset.UtcNow, force: true, out var text));
        Assert.Equal("b\nc\nd\n", text);
    }

    [Fact]
    public void TryFlush_HonorsInterval_UntilForced()
    {
        var buf = new BuildLogBuffer(capacity: 50, flushInterval: TimeSpan.FromMilliseconds(200));
        var t0 = DateTimeOffset.UtcNow;
        buf.Append("one");
        Assert.True(buf.TryFlush(t0, force: false, out var first));
        Assert.Equal("one\n", first);
        buf.Append("two");
        Assert.False(buf.TryFlush(t0.AddMilliseconds(50), force: false, out var held));
        Assert.Equal("one\n", held);
        Assert.True(buf.TryFlush(t0.AddMilliseconds(50), force: true, out var forced));
        Assert.Equal("one\ntwo\n", forced);
    }

    [Fact]
    public void Clear_ResetsText()
    {
        var buf = new BuildLogBuffer();
        buf.Append("x");
        buf.TryFlush(DateTimeOffset.UtcNow, force: true, out _);
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.False(buf.TryFlush(DateTimeOffset.UtcNow, force: true, out var text));
        Assert.Equal("", text);
    }
}

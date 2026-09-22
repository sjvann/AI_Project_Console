namespace AiProject.Console.Core.Catalog;

public readonly record struct ServiceActionFlags(
    bool Open,
    bool Start,
    bool Restart,
    bool Stop,
    bool SelfHint,
    bool HostedHint);

public static class ServiceActionPolicy
{
    public static ServiceActionFlags ForRow(ServiceEntry svc, bool online, bool self)
    {
        var hasUrl = !string.IsNullOrEmpty(svc.OpenUrl);
        // 離線也可「開啟」：先起自己與 dependsOn，再開瀏覽器（隨宿主仍須宿主在線）。
        if (self)
            return new(hasUrl, false, false, false, true, false);
        if (!string.IsNullOrEmpty(svc.HostedBy))
            return new(online && hasUrl, false, false, false, false, true);
        if (online)
            return new(hasUrl, false, true, true, false, false);
        return new(hasUrl, true, false, false, false, false);
    }

    public static ServiceActionFlags ForGroup(IEnumerable<(ServiceEntry Svc, bool Online, bool Self)> members)
    {
        var open = false;
        var start = false;
        var restart = false;
        var stop = false;
        foreach (var (svc, online, self) in members)
        {
            var flags = ForRow(svc, online, self);
            open |= flags.Open;
            start |= flags.Start;
            restart |= flags.Restart;
            stop |= flags.Stop;
        }
        return new(open, start, restart, stop, false, false);
    }
}

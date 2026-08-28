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
        var open = online && !string.IsNullOrEmpty(svc.OpenUrl);
        if (self)
            return new(open, false, false, false, true, false);
        if (!string.IsNullOrEmpty(svc.HostedBy))
            return new(open, false, false, false, false, true);
        if (online)
            return new(open, false, true, true, false, false);
        return new(false, true, false, false, false, false);
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

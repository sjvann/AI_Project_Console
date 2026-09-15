using AiProject.Console.Core.Build;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    OccupancySnapshot _occupancy = OccupancySnapshot.Idle;
    bool _occupancyOwned;
    string? _occupancyRoot;
    CancellationTokenSource? _jobCts;
    readonly BuildLogBuffer _buildLog = new();

    public OccupancySnapshot Occupancy => _occupancy;
    public string BusyReason => _occupancy.BusyReason;
    public CancellationToken JobToken => _jobCts?.Token ?? CancellationToken.None;

    public void CancelOccupancy()
    {
        if (!_occupancy.CanCancel || !_occupancyOwned)
            return;
        try
        {
            _jobCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 工作剛結束
        }
        JobText = "正在取消…";
        Notify();
    }

    bool TryBeginOccupancy(string title, OccupancyKind? kind = null)
    {
        var inferred = kind ?? OccupancyKindLookup.Infer(title);
        if (_occupancy.IsActive)
        {
            if (string.IsNullOrEmpty(_occupancy.QueuedTitle))
                _occupancy = _occupancy.WithQueued(title);
            Notify();
            WarnBusy();
            return false;
        }

        if (Catalog is not null)
        {
            var foreign = WorkspaceOccupancyLock.TryReadLive(Catalog.Root);
            if (foreign is not null && foreign.Pid != Environment.ProcessId)
            {
                AdoptForeignOccupancy(foreign);
                WarnBusy();
                return false;
            }
        }

        _jobCts?.Dispose();
        _jobCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _occupancy = OccupancySnapshot.Begin(
            inferred,
            title,
            OccupancySources.Desktop,
            Environment.ProcessId);
        _occupancyOwned = true;
        _occupancyRoot = Catalog?.Root;
        JobText = title;
        if (!string.IsNullOrEmpty(_occupancyRoot))
            WorkspaceOccupancyLock.TryAcquire(_occupancyRoot, _occupancy.ToRecord(), out _);
        Notify();
        return true;
    }

    void EndOccupancy()
    {
        if (_occupancyOwned && !string.IsNullOrEmpty(_occupancyRoot))
            WorkspaceOccupancyLock.TryRelease(_occupancyRoot, Environment.ProcessId);
        _occupancyOwned = false;
        _occupancyRoot = null;
        try
        {
            _jobCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        _jobCts = null;
        _occupancy = OccupancySnapshot.Idle;
        FlushBuildLog(force: true);
    }

    void WarnBusy() =>
        _native.Info("忙碌中", _occupancy.IsActive ? _occupancy.BusyDialogBody : "請等待目前工作完成。");

    void PublishOccupancyProgress()
    {
        if (!_occupancyOwned || !_occupancy.IsActive)
            return;
        var current = string.IsNullOrWhiteSpace(BuildCurrentName) ? null : BuildCurrentName;
        _occupancy = _occupancy.WithProgress(BuildDone, BuildTotal, current);
        if (!string.IsNullOrEmpty(_occupancyRoot))
            WorkspaceOccupancyLock.Heartbeat(_occupancyRoot, _occupancy.ToRecord());
    }

    void ObserveForeignOccupancy()
    {
        if (_occupancyOwned || Catalog is null)
            return;
        var foreign = WorkspaceOccupancyLock.TryReadLive(Catalog.Root);
        if (foreign is not null && foreign.Pid != Environment.ProcessId)
        {
            AdoptForeignOccupancy(foreign);
            return;
        }

        if (_occupancy.IsActive && !_occupancyOwned)
        {
            _occupancy = OccupancySnapshot.Idle;
            if (JobText.StartsWith("Agent", StringComparison.Ordinal) || JobText.Contains("正在編譯"))
                JobText = "完成";
        }
    }

    void AdoptForeignOccupancy(OccupancyRecord foreign)
    {
        _occupancy = OccupancySnapshot.FromRecord(foreign);
        _occupancyOwned = false;
        JobText = _occupancy.BarTitle;
    }

    void AppendBuild(string line)
    {
        _buildLog.Append(line);
    }

    void FlushBuildLog(bool force)
    {
        if (!_buildLog.TryFlush(DateTimeOffset.UtcNow, force, out var text))
            return;
        BuildText = text;
        Notify();
    }

    void ClearBuildLog()
    {
        _buildLog.Clear();
        BuildText = "";
    }
}

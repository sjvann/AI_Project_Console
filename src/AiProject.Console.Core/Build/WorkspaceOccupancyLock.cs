using System.Diagnostics;
using System.Text.Json;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Build;

/// <summary>
/// 同一工作區 Root 的佔用登錄（AD-32／AD-34）。桌面與 MCP 互斥建置。
/// 檔案在 <c>.ai_project/occupancy.lock.json</c>，不進 git。
/// </summary>
public static class WorkspaceOccupancyLock
{
    public const string FileName = "occupancy.lock.json";

    public static string PathFor(string workspaceRoot) =>
        Path.Combine(workspaceRoot, AppInfo.RuntimeDirName, FileName);

    public static bool TryAcquire(string workspaceRoot, OccupancyRecord record, out OccupancyRecord? blocker)
    {
        blocker = null;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return false;

        var live = TryReadLive(workspaceRoot);
        if (live is not null && live.Pid != record.Pid)
        {
            blocker = live;
            return false;
        }

        Write(workspaceRoot, record);
        return true;
    }

    public static void Heartbeat(string workspaceRoot, OccupancyRecord record)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || record.Pid <= 0)
            return;
        var live = TryRead(workspaceRoot);
        if (live is not null && live.Pid != record.Pid)
            return;
        Write(workspaceRoot, record);
    }

    public static void TryRelease(string workspaceRoot, int pid)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return;
        var current = TryRead(workspaceRoot);
        if (current is null)
            return;
        if (current.Pid != pid)
            return;
        TryDelete(workspaceRoot);
    }

    public static OccupancyRecord? TryReadLive(string workspaceRoot)
    {
        var rec = TryRead(workspaceRoot);
        if (rec is null)
            return null;
        if (rec.Pid <= 0 || !IsProcessAlive(rec.Pid))
        {
            TryDelete(workspaceRoot);
            return null;
        }
        return rec;
    }

    public static OccupancyRecord? TryRead(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;
        var path = PathFor(workspaceRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<OccupancyRecord>(json, JsonUtil.Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void Write(string workspaceRoot, OccupancyRecord record)
    {
        var path = PathFor(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(record, JsonUtil.Options);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    static void TryDelete(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 鎖檔清不掉時下次讀會依 pid 過期
        }
    }
}

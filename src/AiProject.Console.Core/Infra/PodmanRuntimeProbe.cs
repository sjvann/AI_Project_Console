using System.Text.Json.Nodes;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Infra;

public enum PodmanMachineObservation
{
    Running,
    Starting,
    Stopped,
}

/// <summary>一次 Podman 探測的原始結果。測試直接組這個，不呼叫本機 podman。</summary>
public sealed record PodmanSnapshot(
    bool CliPresent,
    bool ListSucceeded,
    IReadOnlyDictionary<string, PodmanMachineObservation> Machines,
    bool ContainersKnown,
    IReadOnlySet<string> RunningContainers,
    string? Detail)
{
    static Dictionary<string, PodmanMachineObservation> EmptyMachines { get; } = new(StringComparer.OrdinalIgnoreCase);

    static HashSet<string> EmptyNames { get; } = new(StringComparer.Ordinal);

    public static PodmanSnapshot NoCli { get; } = new(false, false, EmptyMachines, false, EmptyNames, null);

    public static PodmanSnapshot NotNeeded { get; } = new(true, true, EmptyMachines, false, EmptyNames, null);

    public static PodmanSnapshot ListFailed(string? detail) =>
        new(true, false, EmptyMachines, false, EmptyNames, TrimDetail(detail));

    public static string? TrimDetail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var line = text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        var cut = line.IndexOf('\n');
        if (cut >= 0)
            line = line[..cut].Trim();
        return line.Length <= 240 ? line : line[..239] + "…";
    }
}

public static class PodmanMachineListParser
{
    public static IReadOnlyDictionary<string, PodmanMachineObservation> Parse(string? json)
    {
        var map = new Dictionary<string, PodmanMachineObservation>(StringComparer.OrdinalIgnoreCase);
        json = JsonBody(json);
        if (string.IsNullOrWhiteSpace(json))
            return map;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return map;
        }
        if (node is JsonObject single)
        {
            Add(map, single);
            return map;
        }
        if (node is not JsonArray arr)
            return map;
        foreach (var item in arr)
        {
            if (item is JsonObject obj)
                Add(map, obj);
        }
        return map;
    }

    static string? JsonBody(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;
        var arrayAt = json.IndexOf('[');
        var objectAt = json.IndexOf('{');
        var start = arrayAt < 0 ? objectAt : objectAt < 0 ? arrayAt : Math.Min(arrayAt, objectAt);
        return start <= 0 ? json : json[start..];
    }

    static void Add(Dictionary<string, PodmanMachineObservation> map, JsonObject obj)
    {
        var name = FirstString(obj, "Name", "name");
        if (string.IsNullOrEmpty(name))
            return;
        map[name] = Observe(obj);
    }

    static PodmanMachineObservation Observe(JsonObject obj)
    {
        if (Flag(obj, "Running", "running"))
            return PodmanMachineObservation.Running;
        var state = FirstString(obj, "State", "state");
        if (state.Equals("running", StringComparison.OrdinalIgnoreCase))
            return PodmanMachineObservation.Running;
        if (Flag(obj, "Starting", "starting") || state.Equals("starting", StringComparison.OrdinalIgnoreCase))
            return PodmanMachineObservation.Starting;
        return PodmanMachineObservation.Stopped;
    }

    static bool Flag(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is not JsonValue value)
                continue;
            if (value.TryGetValue<bool>(out var flag))
                return flag;
            var text = value.ToString().Trim();
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string FirstString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is not JsonValue value)
                continue;
            if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }
        return "";
    }
}

public static class WorkspaceRuntimeProbe
{
    public const int CommandTimeoutMs = 4000;

    public static async Task<WorkspaceRuntimeReport> ProbeAsync(ProjectCatalog catalog, CancellationToken ct = default)
    {
        if (!WorkspaceRuntimeManifest.Declared(catalog))
            return WorkspaceRuntimeReport.Empty;
        try
        {
            var snapshot = await CaptureAsync(catalog.Runtimes, ct).ConfigureAwait(false);
            return WorkspaceRuntimeEvaluator.Evaluate(catalog, snapshot, ProcessSupervisor.TcpOpen);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return WorkspaceRuntimeEvaluator.Unreachable(catalog, ex.Message);
        }
    }

    public static async Task<PodmanSnapshot> CaptureAsync(IReadOnlyList<RuntimeDeclaration> runtimes, CancellationToken ct = default)
    {
        if (!runtimes.Any(r => r.IsPodmanMachine))
            return PodmanSnapshot.NotNeeded;
        if (!CliUtil.CommandExists("podman"))
            return PodmanSnapshot.NoCli;

        var (code, output) = await CliUtil.RunAsync(
            "podman",
            ["machine", "list", "--format", "json"],
            timeoutMs: CommandTimeoutMs,
            ct: ct).ConfigureAwait(false);
        if (code != 0)
            return PodmanSnapshot.ListFailed(output);

        var machines = PodmanMachineListParser.Parse(output);
        var anyRunning = runtimes.Any(r =>
            r.IsPodmanMachine
            && machines.TryGetValue(r.Machine, out var state)
            && state == PodmanMachineObservation.Running);
        if (!anyRunning)
            return new PodmanSnapshot(true, true, machines, false, new HashSet<string>(StringComparer.Ordinal), null);

        var (psCode, psOut) = await CliUtil.RunAsync(
            "podman",
            ["ps", "--format", "{{.Names}}"],
            timeoutMs: CommandTimeoutMs,
            ct: ct).ConfigureAwait(false);
        if (psCode != 0)
        {
            return new PodmanSnapshot(
                true,
                true,
                machines,
                false,
                new HashSet<string>(StringComparer.Ordinal),
                PodmanSnapshot.TrimDetail(psOut));
        }
        return new PodmanSnapshot(true, true, machines, true, PodmanContainerNames.Parse(psOut), null);
    }
}

public static class PodmanContainerNames
{
    public static IReadOnlySet<string> Parse(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
            return set;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrEmpty(part))
                    set.Add(part);
            }
        }
        return set;
    }
}

public static class WorkspaceRuntimeEvaluator
{
    public static WorkspaceRuntimeReport Evaluate(
        ProjectCatalog catalog,
        PodmanSnapshot snapshot,
        Func<int, bool>? tcpOpen = null)
    {
        tcpOpen ??= _ => false;
        var runtimes = new List<RuntimeProbeItem>();
        var byId = new Dictionary<string, RuntimeProbeItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var declared in catalog.Runtimes)
        {
            var item = ProbeRuntime(declared, snapshot);
            runtimes.Add(item);
            byId[item.Id] = item;
        }

        var stores = new List<DatastoreProbeItem>();
        foreach (var store in catalog.Datastores)
            stores.Add(ProbeStore(store, byId, snapshot, tcpOpen));
        return new WorkspaceRuntimeReport(runtimes, stores);
    }

    public static WorkspaceRuntimeReport Unreachable(ProjectCatalog catalog, string? detail)
    {
        var note = PodmanSnapshot.TrimDetail(detail);
        var runtimes = new List<RuntimeProbeItem>();
        var byId = new Dictionary<string, RuntimeProbeItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var declared in catalog.Runtimes)
        {
            var item = declared.IsPodmanMachine
                ? Blocked(declared, RuntimeMessages.Unreachable(declared.Machine), note, StartHowTo(declared.Machine))
                : Blocked(declared, RuntimeMessages.Unsupported(declared.Id, declared.Kind), note, null);
            runtimes.Add(item);
            byId[item.Id] = item;
        }
        var stores = catalog.Datastores.Select(store => Inherit(store, byId)).ToList();
        return new WorkspaceRuntimeReport(runtimes, stores);
    }

    static RuntimeProbeItem ProbeRuntime(RuntimeDeclaration declared, PodmanSnapshot snapshot)
    {
        if (!declared.IsPodmanMachine)
            return Blocked(declared, RuntimeMessages.Unsupported(declared.Id, declared.Kind), null, "目前只探測 kind 為 podman-machine 的執行環境。");
        if (!snapshot.CliPresent)
            return Blocked(declared, RuntimeMessages.CliMissing(declared.Machine), null, "安裝 Podman，並確認 podman 在 PATH 上。");
        if (!snapshot.ListSucceeded)
            return Blocked(declared, RuntimeMessages.MachineStopped(declared.Machine), snapshot.Detail, StartHowTo(declared.Machine));
        if (!snapshot.Machines.TryGetValue(declared.Machine, out var observed))
            return Blocked(declared, RuntimeMessages.MachineMissing(declared.Machine), snapshot.Detail, "執行 podman machine list 核對名稱。");
        return observed switch
        {
            PodmanMachineObservation.Running => new RuntimeProbeItem(
                declared.Id,
                declared.Kind,
                declared.Machine,
                RuntimeProbeState.Ready,
                RuntimeMessages.MachineReady(declared.Machine),
                null,
                null),
            PodmanMachineObservation.Starting => Blocked(
                declared,
                RuntimeMessages.MachineStarting(declared.Machine),
                null,
                StartHowTo(declared.Machine)),
            _ => Blocked(declared, RuntimeMessages.MachineStopped(declared.Machine), null, StartHowTo(declared.Machine)),
        };
    }

    static DatastoreProbeItem ProbeStore(
        DatastoreDeclaration store,
        IReadOnlyDictionary<string, RuntimeProbeItem> runtimes,
        PodmanSnapshot snapshot,
        Func<int, bool> tcpOpen)
    {
        if (string.IsNullOrEmpty(store.RuntimeId) || !runtimes.TryGetValue(store.RuntimeId, out var runtime))
        {
            return new DatastoreProbeItem(
                store.Id,
                store.Label,
                store.RuntimeId,
                RuntimeProbeState.Blocked,
                RuntimeMessages.UnknownRuntime(store.Label, store.RuntimeId),
                null,
                "datastores[].runtime 要對到 runtimes[].id。",
                store.RequiredBy);
        }
        if (runtime.State != RuntimeProbeState.Ready)
            return Inherit(store, runtime);

        var reasons = new List<string>();
        if (!string.IsNullOrEmpty(store.Container))
        {
            if (!snapshot.ContainersKnown)
                reasons.Add(snapshot.Detail is null
                    ? $"無法列出容器，不知道 {store.Container} 是否在跑。"
                    : $"無法列出容器（{snapshot.Detail}）。");
            else if (!snapshot.RunningContainers.Contains(store.Container))
                reasons.Add($"容器 {store.Container} 不在執行中。");
        }
        if (store.Port is int port && !tcpOpen(port))
            reasons.Add($"127.0.0.1:{port} 尚未接受連線。");
        if (reasons.Count == 0)
        {
            return new DatastoreProbeItem(
                store.Id,
                store.Label,
                store.RuntimeId,
                RuntimeProbeState.Ready,
                $"{store.Label} 已就緒",
                null,
                null,
                store.RequiredBy);
        }
        var detail = string.Join(" ", reasons);
        return new DatastoreProbeItem(
            store.Id,
            store.Label,
            store.RuntimeId,
            RuntimeProbeState.Blocked,
            RuntimeMessages.DatastoreDown(store.Label),
            detail,
            DatastoreCommand(store),
            store.RequiredBy);
    }

    static DatastoreProbeItem Inherit(DatastoreDeclaration store, IReadOnlyDictionary<string, RuntimeProbeItem> runtimes)
    {
        if (!runtimes.TryGetValue(store.RuntimeId, out var runtime))
        {
            return new DatastoreProbeItem(
                store.Id,
                store.Label,
                store.RuntimeId,
                RuntimeProbeState.Blocked,
                RuntimeMessages.UnknownRuntime(store.Label, store.RuntimeId),
                null,
                null,
                store.RequiredBy);
        }
        return Inherit(store, runtime);
    }

    static DatastoreProbeItem Inherit(DatastoreDeclaration store, RuntimeProbeItem runtime) =>
        new(
            store.Id,
            store.Label,
            store.RuntimeId,
            RuntimeProbeState.Blocked,
            runtime.Headline,
            runtime.Detail,
            DatastoreCommand(store),
            store.RequiredBy);

    /// <summary>
    /// 資料庫列自己的指令。不抄虛擬機那一列，也不把探測錯誤原文當成指令。
    /// </summary>
    static string? DatastoreCommand(DatastoreDeclaration store)
    {
        if (!string.IsNullOrWhiteSpace(store.Start))
            return store.Start.Trim();
        if (!string.IsNullOrWhiteSpace(store.Container))
            return $"podman start {store.Container}";
        return null;
    }

    static RuntimeProbeItem Blocked(RuntimeDeclaration declared, string headline, string? detail, string? howTo) =>
        new(declared.Id, declared.Kind, declared.Machine, RuntimeProbeState.Blocked, headline, detail, howTo);

    static string StartHowTo(string machine) => $"podman machine start {machine}";
}

using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Infra;

/// <summary>
/// 工作區宣告的本機執行環境（目前是 Podman 虛擬機）與資料庫。
/// 編譯不看這層；測試與被點名的服務啟動會看。
/// </summary>
public sealed record RuntimeDeclaration(string Id, string Kind, string Machine)
{
    public bool IsPodmanMachine =>
        Kind.Equals(WorkspaceRuntimeManifest.PodmanMachineKind, StringComparison.OrdinalIgnoreCase);
}

public sealed record DatastoreDeclaration(
    string Id,
    string Label,
    string RuntimeId,
    string Container,
    int? Port,
    IReadOnlyList<string> RequiredBy);

public enum RuntimeProbeState
{
    Ready,
    Blocked,
}

public sealed record RuntimeProbeItem(
    string Id,
    string Kind,
    string Machine,
    RuntimeProbeState State,
    string Headline,
    string? Detail,
    string? HowTo);

public sealed record DatastoreProbeItem(
    string Id,
    string Label,
    string RuntimeId,
    RuntimeProbeState State,
    string Headline,
    string? Detail,
    string? HowTo,
    IReadOnlyList<string> RequiredBy);

public sealed record WorkspaceRuntimeReport(
    IReadOnlyList<RuntimeProbeItem> Runtimes,
    IReadOnlyList<DatastoreProbeItem> Datastores)
{
    public static WorkspaceRuntimeReport Empty { get; } = new([], []);

    public bool HasRows => Runtimes.Count > 0 || Datastores.Count > 0;

    /// <summary>摘要列要顯示的阻斷句。就緒時為空白。</summary>
    public string? Attention
    {
        get
        {
            var lines = new List<string>();
            foreach (var row in Runtimes)
            {
                if (row.State == RuntimeProbeState.Blocked)
                    lines.Add(row.Headline);
            }
            foreach (var row in Datastores)
            {
                if (row.State == RuntimeProbeState.Blocked && !lines.Contains(row.Headline))
                    lines.Add(row.Headline);
            }
            return lines.Count == 0 ? null : string.Join("；", lines);
        }
    }
}

public static class WorkspaceRuntimeManifest
{
    public const string PodmanMachineKind = "podman-machine";
    public const string DefaultMachineName = "podman-machine-default";
    public const string TestToken = "test";

    public static bool Declared(ProjectCatalog catalog) =>
        catalog.Runtimes.Count > 0 || catalog.Datastores.Count > 0;

    public static (IReadOnlyList<RuntimeDeclaration> Runtimes, IReadOnlyList<DatastoreDeclaration> Datastores) Read(JsonObject manifest)
    {
        var runtimes = ReadRuntimes(manifest["runtimes"] ?? manifest["runtime"]);
        var stores = ReadDatastores(manifest["datastores"] ?? manifest["dataStores"] ?? manifest["data_stores"]);
        return (runtimes, stores);
    }

    public static bool IsTestToken(string value) =>
        value.Equals(TestToken, StringComparison.OrdinalIgnoreCase)
        || value.Equals("tests", StringComparison.OrdinalIgnoreCase);

    static List<RuntimeDeclaration> ReadRuntimes(JsonNode? node)
    {
        var list = new List<RuntimeDeclaration>();
        if (node is not JsonArray arr)
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj)
                continue;
            var id = JsonUtil.Str(obj["id"]);
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
                continue;
            var kind = JsonUtil.Pick(JsonUtil.Str(obj["kind"]), JsonUtil.Str(obj["type"]));
            if (kind.Equals("podman_machine", StringComparison.OrdinalIgnoreCase))
                kind = PodmanMachineKind;
            if (string.IsNullOrEmpty(kind))
                kind = PodmanMachineKind;
            var machine = JsonUtil.Pick(JsonUtil.Str(obj["machine"]), JsonUtil.Str(obj["name"]));
            if (string.IsNullOrEmpty(machine))
                machine = DefaultMachineName;
            list.Add(new RuntimeDeclaration(id, kind, machine));
        }
        return list;
    }

    static List<DatastoreDeclaration> ReadDatastores(JsonNode? node)
    {
        var list = new List<DatastoreDeclaration>();
        if (node is not JsonArray arr)
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj)
                continue;
            var id = JsonUtil.Pick(JsonUtil.Str(obj["id"]));
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
                continue;
            var label = JsonUtil.Pick(JsonUtil.Str(obj["label"]), JsonUtil.Str(obj["name"]), id);
            var runtimeId = JsonUtil.Pick(JsonUtil.Str(obj["runtime"]), JsonUtil.Str(obj["runtimeId"]), JsonUtil.Str(obj["runtime_id"]));
            var container = JsonUtil.Pick(JsonUtil.Str(obj["container"]), JsonUtil.Str(obj["containerName"]), JsonUtil.Str(obj["container_name"]));
            var required = ReadRequiredBy(obj["requiredBy"] ?? obj["required_by"]);
            list.Add(new DatastoreDeclaration(id, label, runtimeId, container, ReadPort(obj["port"]), required));
        }
        return list;
    }

    static int? ReadPort(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number) && number > 0)
            return number;
        return int.TryParse(JsonUtil.Str(node), out var parsed) && parsed > 0 ? parsed : null;
    }

    static IReadOnlyList<string> ReadRequiredBy(JsonNode? node)
    {
        if (node is null)
            return [];
        if (node is JsonArray arr)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in arr)
            {
                var text = JsonUtil.Str(item);
                if (!string.IsNullOrEmpty(text) && seen.Add(text))
                    list.Add(text);
            }
            return list;
        }
        var single = JsonUtil.Str(node);
        return string.IsNullOrEmpty(single) ? [] : [single];
    }
}

public static class RuntimeMessages
{
    public static string CliMissing(string machine) =>
        $"找不到 podman 命令，無法確認虛擬機 {machine}。";

    public static string MachineMissing(string machine) =>
        $"Podman 虛擬機不存在：{machine}";

    public static string MachineStopped(string machine) =>
        $"Podman 虛擬機未啟動：{machine}";

    public static string MachineStarting(string machine) =>
        $"Podman 虛擬機啟動中：{machine}";

    public static string MachineReady(string machine) =>
        $"Podman 虛擬機已啟動：{machine}";

    public static string Unreachable(string machine) =>
        $"無法確認 Podman 虛擬機 {machine}。";

    public static string DatastoreDown(string label) =>
        $"虛擬機已啟動，資料庫未就緒：{label}";

    public static string Unsupported(string id, string kind) =>
        $"不支援的執行環境：{id}（{kind}）";

    public static string UnknownRuntime(string label, string runtimeId) =>
        string.IsNullOrEmpty(runtimeId)
            ? $"資料庫 {label} 沒有指向執行環境。"
            : $"資料庫 {label} 指向未知的執行環境 {runtimeId}。";

    public static bool IsBlock(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        return message.StartsWith("找不到 podman", StringComparison.Ordinal)
            || message.StartsWith("Podman 虛擬機未啟動", StringComparison.Ordinal)
            || message.StartsWith("Podman 虛擬機不存在", StringComparison.Ordinal)
            || message.StartsWith("Podman 虛擬機啟動中", StringComparison.Ordinal)
            || message.StartsWith("虛擬機已啟動，資料庫未就緒", StringComparison.Ordinal)
            || message.StartsWith("不支援的執行環境", StringComparison.Ordinal)
            || message.StartsWith("資料庫 ", StringComparison.Ordinal)
            || message.StartsWith("無法確認 Podman", StringComparison.Ordinal);
    }
}

public static class RuntimeGate
{
    public static string? TestMessage(WorkspaceRuntimeReport report)
    {
        var lines = new List<string>();
        foreach (var store in report.Datastores)
        {
            if (store.State != RuntimeProbeState.Blocked)
                continue;
            if (!store.RequiredBy.Any(WorkspaceRuntimeManifest.IsTestToken))
                continue;
            if (!lines.Contains(store.Headline))
                lines.Add(store.Headline);
        }
        return lines.Count == 0 ? null : string.Join("；", lines);
    }

    public static string? ServiceMessage(WorkspaceRuntimeReport report, IEnumerable<string> serviceIds)
    {
        var ids = new HashSet<string>(serviceIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return null;
        var lines = new List<string>();
        foreach (var store in report.Datastores)
        {
            if (store.State != RuntimeProbeState.Blocked)
                continue;
            var hit = store.RequiredBy.Any(token =>
                !WorkspaceRuntimeManifest.IsTestToken(token) && ids.Contains(token));
            if (!hit || lines.Contains(store.Headline))
                continue;
            lines.Add(store.Headline);
        }
        return lines.Count == 0 ? null : string.Join("；", lines);
    }
}

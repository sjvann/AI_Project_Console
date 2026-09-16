using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Runtime;

/// <summary>控制台「申報目的地」連線列（AD-4／AD-16；角色 reporting-destination）。</summary>
public sealed class ReportingDestination
{
    public string Id { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ContractVersion { get; set; } = "1";
    /// <summary>公司核發的回報 API 金鑰（apk_…）。空白則改用 GitHub 權杖。</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>預設 false；僅測試連線通過後可由使用者啟用。</summary>
    public bool Enabled { get; set; }
    public bool? LastTestOk { get; set; }
    public string LastTestMessage { get; set; } = "";
    public DateTimeOffset? LastTestedAt { get; set; }

    public string StatusLabel =>
        Enabled ? "已啟用"
        : LastTestOk == true ? "已測試，尚未啟用"
        : LastTestOk == false ? "測試失敗"
        : "未測試";
}

public static class ReportingDestinationsStore
{
    const string ArrayKey = "reportingDestinations";
    const string SelectedKey = "reportingDestinationSelectedId";

    public static IReadOnlyList<ReportingDestination> Load(JsonObject data)
    {
        var list = new List<ReportingDestination>();
        if (data[ArrayKey] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject obj)
                    continue;
                var id = JsonUtil.Str(obj["id"]);
                var url = JsonUtil.Str(obj["baseUrl"]);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
                    continue;
                list.Add(new ReportingDestination
                {
                    Id = id,
                    DisplayName = string.IsNullOrWhiteSpace(JsonUtil.Str(obj["displayName"])) ? url : JsonUtil.Str(obj["displayName"]),
                    BaseUrl = url.TrimEnd('/'),
                    ContractVersion = string.IsNullOrWhiteSpace(JsonUtil.Str(obj["contractVersion"])) ? "1" : JsonUtil.Str(obj["contractVersion"]),
                    ApiKey = JsonUtil.Str(obj["apiKey"]),
                    Enabled = obj["enabled"]?.GetValue<bool>() ?? false,
                    LastTestOk = obj["lastTestOk"] is null ? null : obj["lastTestOk"]!.GetValue<bool>(),
                    LastTestMessage = JsonUtil.Str(obj["lastTestMessage"]),
                    LastTestedAt = DateTimeOffset.TryParse(JsonUtil.Str(obj["lastTestedAt"]), out var at) ? at : null,
                });
            }
        }

        if (list.Count == 0)
        {
            var legacy = JsonUtil.Str(data["companyBaseUrl"]).Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                list.Add(new ReportingDestination
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DisplayName = "公司工作區",
                    BaseUrl = legacy,
                    ContractVersion = "1",
                    Enabled = false,
                });
            }
        }

        return list;
    }

    /// <summary>讀取並在僅有舊 companyBaseUrl 時寫回清單（穩定 id）。</summary>
    public static IReadOnlyList<ReportingDestination> LoadAndMigrate(JsonObject data)
    {
        var hadArray = data[ArrayKey] is JsonArray { Count: > 0 };
        var list = Load(data).ToList();
        if (!hadArray && list.Count > 0)
            Save(data, list, list[0].Id);
        return list;
    }

    public static void Save(JsonObject data, IReadOnlyList<ReportingDestination> destinations, string? selectedId)
    {
        var arr = new JsonArray();
        foreach (var d in destinations)
        {
            if (string.IsNullOrWhiteSpace(d.BaseUrl))
                continue;
            var obj = new JsonObject
            {
                ["id"] = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("N") : d.Id,
                ["displayName"] = (d.DisplayName ?? "").Trim(),
                ["baseUrl"] = d.BaseUrl.Trim().TrimEnd('/'),
                ["contractVersion"] = string.IsNullOrWhiteSpace(d.ContractVersion) ? "1" : d.ContractVersion.Trim(),
                ["enabled"] = d.Enabled,
            };
            if (!string.IsNullOrWhiteSpace(d.ApiKey))
                obj["apiKey"] = d.ApiKey.Trim();
            if (d.LastTestOk is bool ok)
                obj["lastTestOk"] = ok;
            if (!string.IsNullOrWhiteSpace(d.LastTestMessage))
                obj["lastTestMessage"] = d.LastTestMessage;
            if (d.LastTestedAt is DateTimeOffset at)
                obj["lastTestedAt"] = at.ToString("o");
            arr.Add(obj);
        }
        data[ArrayKey] = arr;
        if (string.IsNullOrWhiteSpace(selectedId))
            data.Remove(SelectedKey);
        else
            data[SelectedKey] = selectedId.Trim();

        // 過渡相容：仍寫第一個已啟用（或第一筆）的 URL，給舊路徑讀
        var primary = destinations.FirstOrDefault(d => d.Enabled) ?? destinations.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.BaseUrl))
            data.Remove("companyBaseUrl");
        else
            data["companyBaseUrl"] = primary.BaseUrl.Trim().TrimEnd('/');
    }

    public static string? SelectedId(JsonObject data) =>
        string.IsNullOrWhiteSpace(JsonUtil.Str(data[SelectedKey])) ? null : JsonUtil.Str(data[SelectedKey]);
}

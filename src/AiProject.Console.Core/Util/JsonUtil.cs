using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiProject.Console.Core.Util;

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonObject LoadObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var node = JsonNode.Parse(text);
            return node as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
        catch (IOException)
        {
            return new JsonObject();
        }
    }

    public static void SaveObject(string path, JsonObject obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, obj.ToJsonString(Options) + "\n", Encoding.UTF8);
    }

    public static string Str(JsonNode? node) =>
        node is null || node is JsonObject or JsonArray ? "" : node.ToString().Trim();

    public static JsonObject? Obj(JsonNode? node) => node as JsonObject;

    public static JsonArray? Arr(JsonNode? node) => node as JsonArray;

    public static string Pick(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return "";
    }
}

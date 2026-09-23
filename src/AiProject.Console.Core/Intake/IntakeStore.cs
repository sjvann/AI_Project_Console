using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Intake;

public static class IntakeStore
{
    public const string RelPath = "docs/product/intake.json";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string root) =>
        System.IO.Path.Combine(System.IO.Path.GetFullPath(root), RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static IntakeDocument Load(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
            return new IntakeDocument();
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<IntakeDocument>(json, JsonOpts);
            return doc ?? new IntakeDocument();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new IntakeDocument();
        }
    }

    public static void Save(string root, IntakeDocument document)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        document.Version = "1";
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOpts) + Environment.NewLine);
    }

    public static IntakeRecord NewRecord(string requester = "")
    {
        var day = DateTime.Now.ToString("yyyyMMdd");
        var record = new IntakeRecord
        {
            Id = $"ISS-{day}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Kind = IntakeKinds.Issue,
            OpenedOn = DateTime.Now.ToString("yyyy-MM-dd"),
            Requester = requester,
            Stage = IntakeStages.Draft,
            Items = [NewWorkItem()],
        };
        return record;
    }

    public static IntakeWorkItem NewWorkItem() =>
        new()
        {
            Id = "WI-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            AcceptanceCriteria = [""],
            AcceptanceDone = [false],
        };

    public static IntakeRecord? Find(IntakeDocument document, string? id) =>
        document.Intakes.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    public static JsonObject EmptyShell() => new()
    {
        ["version"] = "1",
        ["designDocsDir"] = IntakeDesignFiles.DefaultDir,
        ["intakes"] = new JsonArray(),
    };
}

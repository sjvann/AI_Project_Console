using System.Reflection;
using System.Text.Json;
namespace AiProject.Console.Core.Actions;

public static class ActionCatalog
{
    public static IReadOnlyList<ConsoleAction> Load(string? group = null)
    {
        JsonDocument doc;
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("AiProject.Console.Core.Actions.actions.json");
        if (embedded is not null)
        {
            doc = JsonDocument.Parse(embedded);
        }
        else
        {
            var path = Path.Combine(AppContext.BaseDirectory, "actions.json");
            if (!File.Exists(path))
                return [];
            doc = JsonDocument.Parse(File.ReadAllText(path));
        }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("actions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<ConsoleAction>();
            foreach (var a in arr.EnumerateArray())
            {
                var g = a.TryGetProperty("group", out var gv) ? gv.GetString() ?? "" : "";
                if (group is not null && g != group)
                    continue;
                list.Add(new ConsoleAction(
                    Id: a.GetProperty("id").GetString() ?? "",
                    Label: a.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
                    Group: g,
                    Handler: a.TryGetProperty("handler", out var h) ? h.GetString() ?? "" : "",
                    RequiresGithub: a.TryGetProperty("requiresGithub", out var rg) && rg.ValueKind == JsonValueKind.True,
                    RequiresDeploy: a.TryGetProperty("requiresDeploy", out var rd) && rd.ValueKind == JsonValueKind.True,
                    Confirm: a.TryGetProperty("confirm", out var c) ? c.GetString() : null));
            }
            return list;
        }
    }
}

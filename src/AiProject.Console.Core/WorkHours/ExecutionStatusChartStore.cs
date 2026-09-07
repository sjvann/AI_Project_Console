using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.WorkHours;

public sealed class ExecutionStatusChartStore
{
    readonly string _path;
    readonly object _gate = new();
    Dictionary<string, int> _cells = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, string> _notes = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionStatusChartStore(string? path = null)
    {
        _path = path ?? Path.Combine(Path.GetDirectoryName(WorkHoursStore.DefaultPath())!, "status-chart.json");
        Reload();
    }

    public int Intensity(string projectKey, DateOnly date)
    {
        lock (_gate)
            return _cells.TryGetValue(Key(projectKey, date), out var v) ? v : 0;
    }

    public string? Note(string projectKey, DateOnly date)
    {
        lock (_gate)
            return _notes.TryGetValue(Key(projectKey, date), out var n) ? n : null;
    }

    public void Cycle(string projectKey, DateOnly date)
    {
        lock (_gate)
        {
            var key = Key(projectKey, date);
            _cells[key] = (_cells.TryGetValue(key, out var v) ? v : 0) + 1;
            if (_cells[key] > 3)
                _cells.Remove(key);
            Save();
        }
    }

    public IReadOnlyList<(string ProjectKey, DateOnly Date, int Intensity, string? Note)> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<(string, DateOnly, int, string?)>();
            foreach (var kv in _cells)
            {
                var parts = kv.Key.Split('|');
                if (parts.Length != 2 || !DateOnly.TryParse(parts[1], out var date))
                    continue;
                _notes.TryGetValue(kv.Key, out var note);
                list.Add((parts[0], date, kv.Value, note));
            }
            return list;
        }
    }

    void Reload()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            var obj = JsonUtil.LoadObject(_path);
            if (obj["cells"] is JsonObject cells)
            {
                foreach (var kv in cells)
                {
                    if (kv.Value is JsonValue v && v.TryGetValue<int>(out var n))
                        _cells[kv.Key] = n;
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    void Save()
    {
        var cells = new JsonObject();
        foreach (var kv in _cells)
            cells[kv.Key] = kv.Value;
        JsonUtil.SaveObject(_path, new JsonObject { ["cells"] = cells });
    }

    static string Key(string projectKey, DateOnly date) => projectKey + "|" + date.ToString("yyyy-MM-dd");
}

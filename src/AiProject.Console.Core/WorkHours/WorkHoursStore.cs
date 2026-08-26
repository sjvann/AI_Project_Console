using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.WorkHours;

public sealed class WorkHoursStore
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly List<WorkSession> _sessions = [];
    private WorkSession? _current;
    private string _personKey = "";
    private string _personLabel = "";

    public WorkHoursStore(string? path = null, Func<DateTimeOffset>? now = null)
    {
        _path = path ?? DefaultPath();
        _now = now ?? (() => DateTimeOffset.Now);
        Reload();
        if (string.IsNullOrEmpty(_personKey))
        {
            _personKey = WorkHoursPerson.LocalKey();
            _personLabel = WorkHoursPerson.LocalLabel();
        }
    }

    public static string DefaultPath()
    {
        var settings = ConsoleSettingsStore.SettingsPath();
        return Path.Combine(Path.GetDirectoryName(settings)!, "work-hours.json");
    }

    public IReadOnlyList<WorkSession> Sessions
    {
        get
        {
            lock (_gate)
                return _sessions.ToList();
        }
    }

    public WorkSession? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public string PersonKey
    {
        get
        {
            lock (_gate)
                return _personKey;
        }
    }

    public string PersonLabel
    {
        get
        {
            lock (_gate)
                return string.IsNullOrEmpty(_personLabel) ? _personKey : _personLabel;
        }
    }

    public IReadOnlyList<WorkSession> VisibleSessions
    {
        get
        {
            lock (_gate)
                return _sessions.Where(s => WorkHoursPerson.BelongsTo(s.PersonKey, _personKey)).ToList();
        }
    }

    public TimeSpan CurrentDuration()
    {
        lock (_gate)
            return _current?.Duration(_now()) ?? TimeSpan.Zero;
    }

    public TimeSpan TodayDuration()
    {
        var now = _now();
        var today = DateOnly.FromDateTime(now.Date);
        return WorkHoursAggregator.SplitDays(VisibleSessions, now)
            .Where(d => d.Date == today)
            .Select(d => d.Duration)
            .DefaultIfEmpty()
            .Aggregate(TimeSpan.Zero, (a, b) => a + b);
    }

    public WorkSession Start(string? personKey = null, string? personLabel = null)
    {
        lock (_gate)
        {
            ReloadUnlocked();
            CloseOpenSessions(at: null);
            ApplyPerson(personKey, personLabel);
            var now = _now();
            _current = new WorkSession(Guid.NewGuid().ToString("N"), now, null, now, _personKey, _personLabel);
            _sessions.Add(_current.Value);
            Persist();
            return _current.Value;
        }
    }

    /// <summary>
    /// 用 GitHub 帳號把目前時段、以及尚未歸戶的本機時段併到同一人。
    /// </summary>
    public void Identify(string personKey, string? personLabel = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(personKey))
                return;
            ApplyPerson(personKey, personLabel);
            if (_current is { } current)
                ReplaceCurrent(current with { PersonKey = _personKey, PersonLabel = _personLabel });
            if (WorkHoursPerson.IsGithub(_personKey))
            {
                for (var i = 0; i < _sessions.Count; i++)
                {
                    var session = _sessions[i];
                    if (!WorkHoursPerson.CanClaim(session.PersonKey, _personKey))
                        continue;
                    _sessions[i] = session with { PersonKey = _personKey, PersonLabel = _personLabel };
                }
            }
            Persist();
        }
    }

    public void Touch()
    {
        lock (_gate)
        {
            if (_current is null)
                return;
            var now = _now();
            ReplaceCurrent(_current.Value with { LastSeenAt = now });
            Persist();
        }
    }

    public void End()
    {
        lock (_gate)
        {
            if (_current is null)
                return;
            var now = _now();
            ReplaceCurrent(_current.Value with { EndedAt = now, LastSeenAt = now });
            _current = null;
            Persist();
        }
    }

    void Reload()
    {
        lock (_gate)
            ReloadUnlocked();
    }

    void ReloadUnlocked()
    {
        _sessions.Clear();
        _current = null;
        var data = JsonUtil.LoadObject(_path);
        var savedKey = JsonUtil.Str(data["personKey"]);
        if (!string.IsNullOrEmpty(savedKey))
        {
            _personKey = savedKey;
            _personLabel = JsonUtil.Str(data["personLabel"]);
            if (string.IsNullOrEmpty(_personLabel))
                _personLabel = savedKey;
        }
        if (data["sessions"] is not JsonArray arr)
            return;
        foreach (var node in arr)
        {
            var session = Read(node);
            if (session is not null)
                _sessions.Add(session.Value);
        }
    }

    void CloseOpenSessions(DateTimeOffset? at)
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            var session = _sessions[i];
            if (session.EndedAt is not null)
                continue;
            var closed = at ?? session.LastSeenAt;
            if (closed < session.StartedAt)
                closed = session.StartedAt;
            _sessions[i] = session with { EndedAt = closed, LastSeenAt = closed };
        }
        _current = null;
    }

    void ReplaceCurrent(WorkSession session)
    {
        _current = session;
        for (var i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].Id == session.Id)
            {
                _sessions[i] = session;
                return;
            }
        }
        _sessions.Add(session);
    }

    void ApplyPerson(string? personKey, string? personLabel)
    {
        if (string.IsNullOrWhiteSpace(personKey))
        {
            if (!string.IsNullOrEmpty(_personKey))
                return;
            _personKey = WorkHoursPerson.LocalKey();
            _personLabel = WorkHoursPerson.LocalLabel();
            return;
        }
        _personKey = personKey.Trim();
        _personLabel = string.IsNullOrWhiteSpace(personLabel) ? _personKey : personLabel.Trim();
    }

    void Persist()
    {
        var arr = new JsonArray();
        foreach (var session in _sessions)
        {
            var obj = new JsonObject
            {
                ["id"] = session.Id,
                ["startedAt"] = session.StartedAt.ToString("o"),
                ["lastSeenAt"] = session.LastSeenAt.ToString("o"),
                ["personKey"] = session.PersonKey,
                ["personLabel"] = session.PersonLabel,
            };
            if (session.EndedAt is { } ended)
                obj["endedAt"] = ended.ToString("o");
            arr.Add(obj);
        }
        JsonUtil.SaveObject(_path, new JsonObject
        {
            ["personKey"] = _personKey,
            ["personLabel"] = _personLabel,
            ["sessions"] = arr,
        });
    }

    static WorkSession? Read(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;
        var id = JsonUtil.Str(obj["id"]);
        if (string.IsNullOrEmpty(id))
            return null;
        if (!DateTimeOffset.TryParse(JsonUtil.Str(obj["startedAt"]), out var started))
            return null;
        DateTimeOffset? ended = null;
        var endedRaw = JsonUtil.Str(obj["endedAt"]);
        if (!string.IsNullOrEmpty(endedRaw) && DateTimeOffset.TryParse(endedRaw, out var parsedEnd))
            ended = parsedEnd;
        var seen = started;
        if (DateTimeOffset.TryParse(JsonUtil.Str(obj["lastSeenAt"]), out var parsedSeen))
            seen = parsedSeen;
        var personKey = JsonUtil.Str(obj["personKey"]);
        var personLabel = JsonUtil.Str(obj["personLabel"]);
        return new WorkSession(id, started, ended, seen, personKey, personLabel);
    }
}

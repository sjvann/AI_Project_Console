namespace AiProject.Console.Core.Build;

/// <summary>
/// 建置輸出環形緩衝＋合併刷新（AD-33）。呼叫端不得每行整頁重繪。
/// </summary>
public sealed class BuildLogBuffer
{
    public const int DefaultCapacity = 2000;
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromMilliseconds(150);

    readonly object _gate = new();
    readonly Queue<string> _lines = new();
    readonly int _capacity;
    readonly TimeSpan _flushInterval;
    bool _dirty;
    DateTimeOffset _lastFlush;
    string _text = "";

    public BuildLogBuffer(int capacity = DefaultCapacity, TimeSpan? flushInterval = null)
    {
        _capacity = Math.Max(1, capacity);
        _flushInterval = flushInterval ?? DefaultFlushInterval;
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _lines.Count;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _text = "";
            _dirty = false;
            _lastFlush = default;
        }
    }

    public void Append(string line)
    {
        lock (_gate)
        {
            while (_lines.Count >= _capacity)
                _lines.Dequeue();
            _lines.Enqueue(line);
            _dirty = true;
        }
    }

    public bool TryFlush(DateTimeOffset now, bool force, out string text)
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                text = _text;
                return false;
            }

            if (!force && _lastFlush != default && now - _lastFlush < _flushInterval)
            {
                text = _text;
                return false;
            }

            _text = _lines.Count == 0 ? "" : string.Join('\n', _lines) + "\n";
            _lastFlush = now;
            _dirty = false;
            text = _text;
            return true;
        }
    }

    public string Snapshot()
    {
        lock (_gate)
            return _lines.Count == 0 ? "" : string.Join('\n', _lines) + "\n";
    }
}

using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Services;

public sealed class OperationLogStore
{
    public const int Capacity = 10000;

    private readonly object _sync = new();
    private readonly List<OperationLogEntry> _entries = new(Capacity);
    private long _version;
    private int _trimmedCount;

    public event EventHandler? BatchAppended;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public int TrimmedCount
    {
        get
        {
            lock (_sync)
            {
                return _trimmedCount;
            }
        }
    }

    public void Append(OperationLogEntry entry)
    {
        lock (_sync)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.RemoveAt(0);
                _trimmedCount++;
            }

            _entries.Add(entry);
            _version++;
        }

        BatchAppended?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<OperationLogEntry> Snapshot()
    {
        lock (_sync)
        {
            var result = new OperationLogEntry[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
            {
                result[i] = _entries[_entries.Count - 1 - i];
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _trimmedCount = 0;
            _version++;
        }

        BatchAppended?.Invoke(this, EventArgs.Empty);
    }

    public long Version
    {
        get
        {
            lock (_sync)
            {
                return _version;
            }
        }
    }
}
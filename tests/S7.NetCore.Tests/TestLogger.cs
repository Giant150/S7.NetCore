using S7.NetCore;

namespace S7.NetCore.Tests;

internal sealed class TestLogger : IS7Logger
{
    private readonly object _sync = new();

    public List<S7LogEntry> Entries { get; } = [];

    public List<(S7LogLevel Level, S7LogEntry Entry)> Records { get; } = [];

    public bool ThrowOnUse { get; set; }

    public TimeSpan LogDelay { get; set; }

    public S7LogEntry[] SnapshotEntries()
    {
        lock (_sync)
        {
            return Entries.ToArray();
        }
    }

    public bool IsEnabled(S7LogLevel level)
    {
        if (ThrowOnUse)
        {
            throw new InvalidOperationException("logger failure");
        }

        return true;
    }

    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
        if (ThrowOnUse)
        {
            throw new InvalidOperationException("logger failure");
        }

        if (LogDelay > TimeSpan.Zero)
        {
            Thread.Sleep(LogDelay);
        }

        lock (_sync)
        {
            Entries.Add(entry);
            Records.Add((level, entry));
        }
    }
}
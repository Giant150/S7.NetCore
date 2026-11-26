using S7.NetCore;

namespace S7.NetCore.Tests;

internal sealed class TestLogger : IS7Logger
{
    private readonly object _sync = new();

    public List<S7LogEntry> Entries { get; } = [];

    public List<(S7LogLevel Level, S7LogEntry Entry)> Records { get; } = [];

    public List<Exception?> Causes { get; } = [];

    public bool ThrowOnUse { get; set; }

    public TimeSpan LogDelay { get; set; }

    public TimeSpan IsEnabledDelay { get; set; }

    public bool IsEnabledThrows { get; set; }

    public bool LogThrows { get; set; }

    public S7LogEntry[] SnapshotEntries()
    {
        lock (_sync)
        {
            return Entries.ToArray();
        }
    }

    public Exception?[] SnapshotCauses()
    {
        lock (_sync)
        {
            return Causes.ToArray();
        }
    }

    public bool IsEnabled(S7LogLevel level)
    {
        if (IsEnabledDelay > TimeSpan.Zero)
        {
            Thread.Sleep(IsEnabledDelay);
        }

        if (ThrowOnUse || IsEnabledThrows)
        {
            throw new InvalidOperationException("logger failure");
        }

        return true;
    }

    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
        if (LogDelay > TimeSpan.Zero)
        {
            Thread.Sleep(LogDelay);
        }

        if (ThrowOnUse || LogThrows)
        {
            throw new InvalidOperationException("logger failure");
        }

        lock (_sync)
        {
            Entries.Add(entry);
            Records.Add((level, entry));
            Causes.Add(exception);
        }
    }
}
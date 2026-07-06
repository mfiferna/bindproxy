namespace BindProxy.Core.Sessions;

public sealed record ConnectionErrorLogEntry(DateTime TimestampUtc, string NicName, string Message);

/// <summary>
/// A rolling, in-memory record of connection errors across all sessions. Runtime-only: nothing
/// here is persisted. Capped at <paramref name="capacity"/> entries, oldest evicted first.
/// </summary>
public sealed class ConnectionErrorLog(int capacity = 200)
{
    private readonly object _lock = new();
    private readonly Queue<ConnectionErrorLogEntry> _entries = new();

    public event Action? EntryAdded;

    /// <summary>Snapshot of current entries, oldest first.</summary>
    public IReadOnlyList<ConnectionErrorLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToArray(); }
    }

    public void Add(string nicName, string message)
    {
        lock (_lock)
        {
            _entries.Enqueue(new ConnectionErrorLogEntry(DateTime.UtcNow, nicName, message));
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
        EntryAdded?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}

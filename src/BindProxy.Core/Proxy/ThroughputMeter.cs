namespace BindProxy.Core.Proxy;

/// <summary>
/// Tracks cumulative bytes transferred in each direction and a rolling-average rate over
/// <paramref name="window"/>. Runtime-only: nothing here is persisted. Not thread-safe across
/// <see cref="Tick"/> calls (call it from one thread), but <see cref="AddSent"/>/<see cref="AddReceived"/>
/// are safe to call concurrently from many connections.
/// </summary>
public sealed class ThroughputMeter(TimeSpan window, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly Queue<(DateTime Time, long Sent, long Received)> _samples = new();
    private long _totalSent;
    private long _totalReceived;

    public long TotalBytesSent => Interlocked.Read(ref _totalSent);
    public long TotalBytesReceived => Interlocked.Read(ref _totalReceived);
    public double SentBytesPerSecond { get; private set; }
    public double ReceivedBytesPerSecond { get; private set; }

    public void AddSent(int byteCount) => Interlocked.Add(ref _totalSent, byteCount);
    public void AddReceived(int byteCount) => Interlocked.Add(ref _totalReceived, byteCount);

    /// <summary>Samples current totals and recomputes the rolling rate. Call roughly once per second.</summary>
    public void Tick()
    {
        var now = _clock();
        _samples.Enqueue((now, TotalBytesSent, TotalBytesReceived));
        while (_samples.Count > 1 && now - _samples.Peek().Time > window)
        {
            _samples.Dequeue();
        }

        var oldest = _samples.Peek();
        var elapsed = (now - oldest.Time).TotalSeconds;
        if (elapsed <= 0)
        {
            return;
        }

        SentBytesPerSecond = (TotalBytesSent - oldest.Sent) / elapsed;
        ReceivedBytesPerSecond = (TotalBytesReceived - oldest.Received) / elapsed;
    }
}

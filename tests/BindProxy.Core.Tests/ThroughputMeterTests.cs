using BindProxy.Core.Proxy;
using Xunit;

namespace BindProxy.Core.Tests;

public class ThroughputMeterTests
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Totals_reflect_added_bytes_without_needing_a_tick()
    {
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(5), () => Epoch);
        meter.AddSent(500);
        meter.AddSent(250);
        meter.AddReceived(1000);

        Assert.Equal(750, meter.TotalBytesSent);
        Assert.Equal(1000, meter.TotalBytesReceived);
    }

    [Fact]
    public void Rate_is_zero_before_two_samples_exist()
    {
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(5), () => Epoch);
        meter.AddSent(10_000);
        meter.Tick(); // first tick only seeds a sample, nothing to compute a delta against yet

        Assert.Equal(0, meter.SentBytesPerSecond);
    }

    [Fact]
    public void Computes_rate_from_bytes_added_between_two_ticks()
    {
        var now = Epoch;
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(5), () => now);

        meter.Tick(); // seed at t=0, total=0
        meter.AddSent(1000);
        meter.AddReceived(4000);
        now = now.AddSeconds(1);
        meter.Tick(); // t=1, total sent=1000, received=4000

        Assert.Equal(1000, meter.SentBytesPerSecond);
        Assert.Equal(4000, meter.ReceivedBytesPerSecond);
    }

    [Fact]
    public void Old_bursts_age_out_of_the_rolling_window()
    {
        var now = Epoch;
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(5), () => now);

        meter.Tick(); // t=0, total=0
        meter.AddSent(10_000);
        now = now.AddSeconds(1);
        meter.Tick(); // t=1, total=10000
        Assert.Equal(10_000, meter.SentBytesPerSecond);

        // No further traffic; once the burst falls outside the 5s window the rate should
        // reflect only the traffic-free recent history, not the stale burst.
        now = now.AddSeconds(5);
        meter.Tick(); // t=6, total still 10000

        Assert.Equal(0, meter.SentBytesPerSecond);
    }

    [Fact]
    public void Reports_zero_rate_with_no_traffic()
    {
        var now = Epoch;
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(5), () => now);

        meter.Tick();
        now = now.AddSeconds(1);
        meter.Tick();
        now = now.AddSeconds(1);
        meter.Tick();

        Assert.Equal(0, meter.SentBytesPerSecond);
        Assert.Equal(0, meter.ReceivedBytesPerSecond);
    }
}

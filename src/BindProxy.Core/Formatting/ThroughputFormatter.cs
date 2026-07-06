using System.Globalization;

namespace BindProxy.Core.Formatting;

/// <summary>Formats byte counts and byte rates for display, auto-scaling to a fixed unit range.</summary>
public static class ThroughputFormatter
{
    /// <summary>
    /// Formats a byte rate as a network throughput figure: bits per second, floored at Mbps
    /// (never drops to Kbps) and ceilinged at Gbps, one decimal place.
    /// </summary>
    public static string FormatBitsPerSecond(double bytesPerSecond, CultureInfo culture)
    {
        double bits = bytesPerSecond * 8;
        double mbps = Math.Round(bits / 1_000_000.0, 1, MidpointRounding.AwayFromZero);
        if (mbps < 1000)
        {
            return string.Format(culture, "{0:0.0} Mbps", mbps);
        }

        double gbps = Math.Round(bits / 1_000_000_000.0, 1, MidpointRounding.AwayFromZero);
        return string.Format(culture, "{0:0.0} Gbps", gbps);
    }

    /// <summary>
    /// Formats a byte count as a data amount, floored at MB (never drops to KB) and ceilinged
    /// at GB, one decimal place.
    /// </summary>
    public static string FormatBytes(long bytes, CultureInfo culture)
    {
        double mb = Math.Round(bytes / 1_000_000.0, 1, MidpointRounding.AwayFromZero);
        if (mb < 1000)
        {
            return string.Format(culture, "{0:0.0} MB", mb);
        }

        double gb = Math.Round(bytes / 1_000_000_000.0, 1, MidpointRounding.AwayFromZero);
        return string.Format(culture, "{0:0.0} GB", gb);
    }
}

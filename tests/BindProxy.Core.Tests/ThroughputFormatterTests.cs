using System.Globalization;
using BindProxy.Core.Formatting;
using Xunit;

namespace BindProxy.Core.Tests;

public class ThroughputFormatterTests
{
    [Theory]
    [InlineData(0, "0.0 Mbps")]
    [InlineData(125_000, "1.0 Mbps")] // 1,000,000 bits/s
    [InlineData(1_562_500, "12.5 Mbps")] // 12,500,000 bits/s
    [InlineData(124_987_500, "999.9 Mbps")] // 999,900,000 bits/s, just under the Gbps crossover
    [InlineData(125_000_000, "1.0 Gbps")] // exactly 1000 Mbps -> crosses over to Gbps
    [InlineData(312_500_000, "2.5 Gbps")]
    [InlineData(124_995_000, "1.0 Gbps")] // raw 999.96 Mbps rounds to 1000.0, must still show as Gbps
    public void FormatBitsPerSecond_autoscales_between_Mbps_and_Gbps(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, ThroughputFormatter.FormatBitsPerSecond(bytesPerSecond, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0, "0.0 MB")]
    [InlineData(1_000_000, "1.0 MB")]
    [InlineData(999_900_000, "999.9 MB")]
    [InlineData(1_000_000_000, "1.0 GB")]
    [InlineData(2_500_000_000, "2.5 GB")]
    [InlineData(999_960_000, "1.0 GB")] // raw 999.96 MB rounds to 1000.0, must still show as GB
    public void FormatBytes_autoscales_between_MB_and_GB(long bytes, string expected)
    {
        Assert.Equal(expected, ThroughputFormatter.FormatBytes(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatBitsPerSecond_respects_culture_decimal_separator()
    {
        var czech = CultureInfo.GetCultureInfo("cs-CZ");
        Assert.Equal("12,5 Mbps", ThroughputFormatter.FormatBitsPerSecond(1_562_500, czech));
    }

    [Fact]
    public void FormatBytes_respects_culture_decimal_separator()
    {
        var czech = CultureInfo.GetCultureInfo("cs-CZ");
        Assert.Equal("1,5 MB", ThroughputFormatter.FormatBytes(1_500_000, czech));
    }
}

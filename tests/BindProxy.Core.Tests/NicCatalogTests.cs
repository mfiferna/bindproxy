using System.Net.NetworkInformation;
using System.Net.Sockets;
using BindProxy.Core.Nics;
using Xunit;

namespace BindProxy.Core.Tests;

public class NicCatalogTests
{
    [Fact]
    public void Returns_without_throwing()
    {
        var nics = NicCatalog.GetUsableNics();
        Assert.NotNull(nics);
    }

    [Fact]
    public void Every_nic_has_an_ipv4_address_and_id()
    {
        foreach (var nic in NicCatalog.GetUsableNics())
        {
            Assert.False(string.IsNullOrEmpty(nic.Id));
            Assert.False(string.IsNullOrEmpty(nic.Name));
            Assert.Equal(AddressFamily.InterNetwork, nic.Ipv4Address.AddressFamily);
            Assert.All(nic.DnsServers, s => Assert.Equal(AddressFamily.InterNetwork, s.AddressFamily));
        }
    }

    [Fact]
    public void Excludes_loopback()
    {
        Assert.DoesNotContain(NicCatalog.GetUsableNics(),
            n => n.Ipv4Address.Equals(System.Net.IPAddress.Loopback));
    }

    [Fact]
    public void Every_nic_has_an_ipv4_gateway()
    {
        var byId = NetworkInterface.GetAllNetworkInterfaces()
            .ToDictionary(nic => nic.Id, StringComparer.Ordinal);

        foreach (var nic in NicCatalog.GetUsableNics())
        {
            Assert.True(byId.TryGetValue(nic.Id, out var networkInterface));
            Assert.Contains(
                networkInterface!.GetIPProperties().GatewayAddresses,
                gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !System.Net.IPAddress.Any.Equals(gateway.Address));
        }
    }
}

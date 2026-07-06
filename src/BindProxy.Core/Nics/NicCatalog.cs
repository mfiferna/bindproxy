using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BindProxy.Core.Nics;

public static class NicCatalog
{
    public static IReadOnlyList<NicInfo> GetUsableNics()
    {
        var result = new List<NicInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            var ipv4 = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null) continue;
            var dns = props.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
            bool hasIpv4Gateway = props.GatewayAddresses
                .Select(g => g.Address)
                .Any(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(a));
            if (!hasIpv4Gateway) continue;
            result.Add(new NicInfo(nic.Id, nic.Name, nic.Description, ipv4, dns, ClassifyKind(nic.NetworkInterfaceType)));
        }
        return result.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static NicKind ClassifyKind(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NicKind.Wireless,
        NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Ethernet3Megabit
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.GigabitEthernet => NicKind.Ethernet,
        _ => NicKind.Other,
    };
}

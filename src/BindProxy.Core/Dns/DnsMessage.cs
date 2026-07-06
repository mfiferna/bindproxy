using System.Net;
using System.Text;

namespace BindProxy.Core.Dns;

public sealed record DnsAnswer(IReadOnlyList<IPAddress> Addresses, TimeSpan Ttl);

/// <summary>Builds and parses the two DNS packet shapes this app needs: A-record query and response.</summary>
public static class DnsMessage
{
    public static byte[] BuildQuery(ushort id, string hostname)
    {
        var buf = new List<byte>(32)
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00,             // flags: recursion desired
            0x00, 0x01,             // QDCOUNT 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        foreach (var label in hostname.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException($"Invalid hostname label in '{hostname}'", nameof(hostname));
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.Add(0x00);
        buf.AddRange([0x00, 0x01, 0x00, 0x01]); // QTYPE A, QCLASS IN
        return buf.ToArray();
    }

    public static DnsAnswer Parse(ReadOnlySpan<byte> response, ushort expectedId)
    {
        if (response.Length < 12) throw new DnsException("Response too short");
        ushort id = (ushort)(response[0] << 8 | response[1]);
        if (id != expectedId) throw new DnsException("Response transaction id mismatch");
        if ((response[2] & 0x80) == 0) throw new DnsException("Packet is not a response");
        int rcode = response[3] & 0x0F;
        if (rcode != 0) throw new DnsException($"Server returned error rcode {rcode}");
        int qdCount = response[4] << 8 | response[5];
        int anCount = response[6] << 8 | response[7];

        int pos = 12;
        for (int i = 0; i < qdCount; i++)
        {
            SkipName(response, ref pos);
            if (pos + 4 > response.Length) throw new DnsException("Truncated question record");
            pos += 4; // QTYPE + QCLASS
        }

        var addresses = new List<IPAddress>();
        long minTtl = long.MaxValue;
        for (int i = 0; i < anCount; i++)
        {
            SkipName(response, ref pos);
            if (pos + 10 > response.Length) throw new DnsException("Truncated answer record");
            int type = response[pos] << 8 | response[pos + 1];
            int cls = response[pos + 2] << 8 | response[pos + 3];
            long ttl = (long)response[pos + 4] << 24 | (long)response[pos + 5] << 16
                     | (long)response[pos + 6] << 8 | response[pos + 7];
            int rdLength = response[pos + 8] << 8 | response[pos + 9];
            pos += 10;
            if (pos + rdLength > response.Length) throw new DnsException("Truncated answer rdata");
            if (type == 1 && cls == 1 && rdLength == 4)
            {
                addresses.Add(new IPAddress(response.Slice(pos, 4)));
                minTtl = Math.Min(minTtl, ttl);
            }
            pos += rdLength;
        }
        return new DnsAnswer(addresses, addresses.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(minTtl));
    }

    private static void SkipName(ReadOnlySpan<byte> data, ref int pos)
    {
        while (true)
        {
            if (pos >= data.Length) throw new DnsException("Truncated name");
            byte len = data[pos];
            if ((len & 0xC0) == 0xC0)
            {
                if (pos + 1 >= data.Length) throw new DnsException("Truncated compression pointer");
                pos += 2;
                return;
            }
            if (len == 0) { pos += 1; return; }
            if (pos + len + 1 > data.Length) throw new DnsException("Truncated label");
            pos += len + 1;
        }
    }
}

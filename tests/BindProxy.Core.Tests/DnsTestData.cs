using System.Net;

namespace BindProxy.Core.Tests;

internal static class DnsTestData
{
    /// <summary>Builds a valid DNS response to <paramref name="query"/> with one A record.</summary>
    public static byte[] BuildResponse(byte[] query, IPAddress answer, int ttlSeconds)
    {
        var buf = new List<byte>(query.Length + 16)
        {
            query[0], query[1],   // same transaction id
            0x81, 0x80,           // QR=1 (response), RD, RA, rcode 0
            0x00, 0x01,           // QDCOUNT 1
            0x00, 0x01,           // ANCOUNT 1
            0x00, 0x00, 0x00, 0x00,
        };
        buf.AddRange(query[12..]);              // echo the question section
        buf.AddRange([0xC0, 0x0C]);             // answer name: pointer to offset 12
        buf.AddRange([0x00, 0x01, 0x00, 0x01]); // type A, class IN
        buf.AddRange([(byte)(ttlSeconds >> 24), (byte)(ttlSeconds >> 16), (byte)(ttlSeconds >> 8), (byte)ttlSeconds]);
        buf.AddRange([0x00, 0x04]);             // RDLENGTH 4
        buf.AddRange(answer.GetAddressBytes());
        return buf.ToArray();
    }
}

using System.Net;
using BindProxy.Core.Dns;
using Xunit;

namespace BindProxy.Core.Tests;

public class DnsMessageTests
{
    [Fact]
    public void Builds_a_query_for_hostname()
    {
        var query = DnsMessage.BuildQuery(0x2A, "example.com");
        byte[] expected =
        [
            0x00, 0x2A,             // id
            0x01, 0x00,             // flags: RD
            0x00, 0x01,             // QDCOUNT 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m',
            0,
            0x00, 0x01,             // QTYPE A
            0x00, 0x01,             // QCLASS IN
        ];
        Assert.Equal(expected, query);
    }

    [Fact]
    public void Rejects_invalid_hostname_label()
    {
        Assert.Throws<ArgumentException>(() => DnsMessage.BuildQuery(1, "bad..host"));
    }

    [Fact]
    public void Parses_response_with_compressed_name()
    {
        var query = DnsMessage.BuildQuery(0x2A, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Parse("93.184.216.34"), 60);
        var answer = DnsMessage.Parse(response, 0x2A);
        Assert.Equal(new[] { IPAddress.Parse("93.184.216.34") }, answer.Addresses);
        Assert.Equal(TimeSpan.FromSeconds(60), answer.Ttl);
    }

    [Fact]
    public void Parses_response_with_cname_before_a_record()
    {
        var query = DnsMessage.BuildQuery(7, "example.com");
        var buf = new List<byte>
        {
            0x00, 0x07, 0x81, 0x80,
            0x00, 0x01,             // QDCOUNT
            0x00, 0x02,             // ANCOUNT 2
            0x00, 0x00, 0x00, 0x00,
        };
        buf.AddRange(query[12..]);
        // CNAME record: name ptr, type 5, class 1, ttl 30, rdlength 2, rdata = pointer (lazy but legal)
        buf.AddRange([0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x1E, 0x00, 0x02, 0xC0, 0x0C]);
        // A record: name ptr, type 1, class 1, ttl 60, rdlength 4, 1.2.3.4
        buf.AddRange([0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 1, 2, 3, 4]);
        var answer = DnsMessage.Parse(buf.ToArray(), 7);
        Assert.Equal(new[] { IPAddress.Parse("1.2.3.4") }, answer.Addresses);
        Assert.Equal(TimeSpan.FromSeconds(60), answer.Ttl);
    }

    [Fact]
    public void Throws_on_id_mismatch()
    {
        var query = DnsMessage.BuildQuery(1, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Loopback, 60);
        Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 2));
    }

    [Fact]
    public void Throws_on_error_rcode()
    {
        var query = DnsMessage.BuildQuery(1, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Loopback, 60);
        response[3] = (byte)(response[3] & 0xF0 | 0x03); // NXDOMAIN
        var ex = Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 1));
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void Throws_on_truncated_question_record()
    {
        byte[] response =
        [
            0x00, 0x07, 0x81, 0x80,
            0x00, 0x01, // QDCOUNT
            0x00, 0x00, // ANCOUNT
            0x00, 0x00, 0x00, 0x00,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m',
            0,
            0x00, 0x01, 0x00, // truncated QCLASS
        ];

        var ex = Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 7));
        Assert.Contains("Truncated question", ex.Message);
    }

    [Fact]
    public void Throws_on_truncated_compression_pointer()
    {
        byte[] response =
        [
            0x00, 0x07, 0x81, 0x80,
            0x00, 0x01, // QDCOUNT
            0x00, 0x01, // ANCOUNT
            0x00, 0x00, 0x00, 0x00,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m',
            0,
            0x00, 0x01, 0x00, 0x01,
            0xC0, // truncated answer name pointer
        ];

        var ex = Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 7));
        Assert.Contains("Truncated compression pointer", ex.Message);
    }
}

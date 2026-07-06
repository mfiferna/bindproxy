using BindProxy.Core.Sessions;
using Xunit;

namespace BindProxy.Core.Tests;

public class ConnectionErrorLogTests
{
    [Fact]
    public void Add_records_an_entry_with_nic_name_and_message()
    {
        var log = new ConnectionErrorLog();
        log.Add("Ethernet 2", "connect timeout: youtube.com:443");

        var entry = Assert.Single(log.Entries);
        Assert.Equal("Ethernet 2", entry.NicName);
        Assert.Equal("connect timeout: youtube.com:443", entry.Message);
    }

    [Fact]
    public void Entries_are_returned_oldest_first()
    {
        var log = new ConnectionErrorLog();
        log.Add("Wi-Fi", "first");
        log.Add("Wi-Fi", "second");
        log.Add("Wi-Fi", "third");

        Assert.Equal(["first", "second", "third"], log.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Add_raises_EntryAdded()
    {
        var log = new ConnectionErrorLog();
        int fired = 0;
        log.EntryAdded += () => fired++;

        log.Add("Wi-Fi", "oops");

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Oldest_entries_are_evicted_once_over_capacity()
    {
        var log = new ConnectionErrorLog(capacity: 3);
        log.Add("Wi-Fi", "1");
        log.Add("Wi-Fi", "2");
        log.Add("Wi-Fi", "3");
        log.Add("Wi-Fi", "4");

        Assert.Equal(["2", "3", "4"], log.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var log = new ConnectionErrorLog();
        log.Add("Wi-Fi", "oops");
        log.Clear();

        Assert.Empty(log.Entries);
    }
}

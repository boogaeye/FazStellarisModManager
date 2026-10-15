using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Tests;

public class RecentHostsTests
{
    [Fact]
    public void Add_puts_the_address_first()
    {
        Assert.Equal(["a:1"], RecentHosts.Add(null, "a:1"));
        Assert.Equal(["b:2", "a:1"], RecentHosts.Add(["a:1"], " b:2 "));
    }

    [Fact]
    public void Add_moves_an_existing_address_to_the_front_ignoring_case()
    {
        Assert.Equal(["Host:1", "a:1", "c:3"], RecentHosts.Add(["a:1", "host:1", "c:3"], "Host:1"));
    }

    [Fact]
    public void Add_keeps_at_most_five()
    {
        var list = RecentHosts.Add(["a:1", "b:1", "c:1", "d:1", "e:1"], "f:1");

        Assert.Equal(RecentHosts.Max, list.Count);
        Assert.Equal(["f:1", "a:1", "b:1", "c:1", "d:1"], list);
    }

    [Fact]
    public void Add_ignores_a_blank_address()
    {
        Assert.Equal(["a:1"], RecentHosts.Add(["a:1"], "  "));
        Assert.Empty(RecentHosts.Add(null, ""));
    }

    [Fact]
    public void Remove_drops_the_address_ignoring_case()
    {
        Assert.Equal(["a:1", "c:3"], RecentHosts.Remove(["a:1", "B:2", "c:3"], " b:2"));
        Assert.Equal(["a:1"], RecentHosts.Remove(["a:1"], "x:9"));
        Assert.Empty(RecentHosts.Remove(null, "a:1"));
    }
}

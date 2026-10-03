using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Session;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SessionProtocolTests
{
    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4", SessionProtocol.DefaultPort)]
    [InlineData(" myhost:1234 ", "myhost", 1234)]
    [InlineData("10.0.0.5:27015", "10.0.0.5", 27015)]
    [InlineData("::1", "::1", 27015)]
    [InlineData("[::1]", "::1", 27015)]
    [InlineData("[::1]:5000", "::1", 5000)]
    public void Parses_addresses(string input, string host, int port) =>
        Assert.Equal((host, port), SessionProtocol.ParseAddress(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":80")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("host:abc")]
    [InlineData("host:")]
    [InlineData("http://x")]
    [InlineData("host:+80")]
    [InlineData("[::1")]
    public void Rejects_bad_addresses(string input) =>
        Assert.Throws<ArgumentException>(() => SessionProtocol.ParseAddress(input));

    [Fact]
    public void Status_reflects_match_mismatch_and_reliability()
    {
        var host = TestSnapshots.Machine("H", "ugc:1", "ugc:2");

        Assert.Equal(PlayerStatus.Ready, SessionProtocol.StatusFor(ModDiffer.Diff(host, TestSnapshots.Machine("A", "ugc:1", "ugc:2"))));
        Assert.Equal(PlayerStatus.Mismatch, SessionProtocol.StatusFor(ModDiffer.Diff(host, TestSnapshots.Machine("B", "ugc:1"))));
        var withWarnings = TestSnapshots.Machine("C", "ugc:1", "ugc:2") with { Warnings = ["  [unreadable] x"] };
        Assert.Equal(PlayerStatus.Unreliable, SessionProtocol.StatusFor(ModDiffer.Diff(host, withWarnings)));
    }

    [Fact]
    public void Missing_mod_with_warnings_is_a_mismatch()
    {
        var host = TestSnapshots.Machine("H", "ugc:1", "ugc:2");
        var mine = TestSnapshots.Machine("C", "ugc:1") with { Warnings = ["  [unreadable] x"] };

        Assert.Equal(PlayerStatus.Mismatch, SessionProtocol.StatusFor(ModDiffer.Diff(host, mine)));
    }

    [Fact]
    public void Summary_counts_and_describes_differences()
    {
        var host = TestSnapshots.Machine("H", "ugc:1", "ugc:2", "ugc:3");
        var mine = TestSnapshots.Machine("M", "ugc:3", "ugc:1");

        var s = DiffSummary.From(ModDiffer.Diff(host, mine));

        Assert.Equal((1, 0, 0, 1), (s.Missing, s.Extra, s.Different, s.OutOfOrder));
        Assert.Equal("1 missing, load order differs", s.ToString());
        Assert.Equal("matches", DiffSummary.From(ModDiffer.Diff(host, host)).ToString());
    }
}

using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Tests;

public class SessionViewTests
{
    static PlayerInfo Player(PlayerStatus status, bool isHost = false, DiffSummary? summary = null, string? activity = null, string name = "P") =>
        new("id", name, isHost, status, summary, activity);

    [Fact]
    public void Count_groups_players_by_ring_colour()
    {
        var c = SessionView.Count([
            Player(PlayerStatus.Ready, isHost: true), Player(PlayerStatus.Ready), Player(PlayerStatus.Mismatch),
            Player(PlayerStatus.Unreliable), Player(PlayerStatus.Waiting), Player(PlayerStatus.Busy), Player(PlayerStatus.Busy),
        ]);

        Assert.Equal(new SessionView.ReadyCounts(2, 2, 3), c);
        Assert.Equal(7, c.Total);
    }

    [Fact]
    public void RingGradient_gives_green_red_then_amber_shares()
    {
        Assert.Equal("conic-gradient(var(--ok) 0 33.33%, var(--danger) 33.33% 66.67%, var(--warn) 66.67% 100%)",
            SessionView.RingGradient(new SessionView.ReadyCounts(1, 1, 1)));
        Assert.Equal("conic-gradient(var(--ok) 0 50%, var(--danger) 50% 100%, var(--warn) 100% 100%)",
            SessionView.RingGradient(new SessionView.ReadyCounts(1, 1, 0)));
        Assert.Equal("conic-gradient(var(--ok) 0 100%, var(--danger) 100% 100%, var(--warn) 100% 100%)",
            SessionView.RingGradient(new SessionView.ReadyCounts(3, 0, 0)));
    }

    [Fact]
    public void RingGradient_without_players_is_a_plain_ring()
    {
        Assert.Equal("var(--border)", SessionView.RingGradient(default));
    }

    [Fact]
    public void AvatarHue_is_stable_and_in_range()
    {
        Assert.Equal(SessionView.AvatarHue("SCP Fazbear"), SessionView.AvatarHue("SCP Fazbear"));
        Assert.NotEqual(SessionView.AvatarHue("FLAMGOP"), SessionView.AvatarHue("XNPH"));
        Assert.Equal(0, SessionView.AvatarHue(null));
        Assert.InRange(SessionView.AvatarHue(new string((char)0xFFFF, 500)), 0, 359);
        // h = (h * 31 + c) % 360 over "AB": (65 * 31 + 66) % 360 = 281
        Assert.Equal(281, SessionView.AvatarHue("AB"));
    }

    [Theory]
    [InlineData("fazbear", "F")]
    [InlineData("  [TAG] zed", "T")]
    [InlineData("1st", "1")]
    [InlineData("élan", "É")]
    [InlineData("--", "?")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initial_is_the_first_letter_or_digit(string? name, string expected)
    {
        Assert.Equal(expected, SessionView.Initial(name));
    }

    [Fact]
    public void SubLine_names_the_host_and_you()
    {
        Assert.Equal("host · you", SessionView.SubLine(Player(PlayerStatus.Ready, isHost: true), you: true));
        Assert.Equal("host", SessionView.SubLine(Player(PlayerStatus.Unreliable, isHost: true), you: false));
        Assert.Equal("you · Ready", SessionView.SubLine(Player(PlayerStatus.Ready), you: true));
    }

    [Fact]
    public void SubLine_shows_the_summary_activity_or_state()
    {
        var summary = new DiffSummary(true, 0, 0, 2, 0, 24, 0);

        Assert.Equal("2 missing · 24 different", SessionView.SubLine(Player(PlayerStatus.Mismatch, summary: summary), you: false));
        Assert.Equal("differs", SessionView.SubLine(Player(PlayerStatus.Mismatch), you: false));
        Assert.Equal("Scanning mods… 40%", SessionView.SubLine(Player(PlayerStatus.Busy, activity: "Scanning mods… 40%"), you: false));
        Assert.Equal("Busy…", SessionView.SubLine(Player(PlayerStatus.Busy), you: false));
        Assert.Equal("Waiting for scan…", SessionView.SubLine(Player(PlayerStatus.Waiting), you: false));
        Assert.Equal("check scan warnings", SessionView.SubLine(Player(PlayerStatus.Unreliable), you: false));
    }

    [Fact]
    public void Short_lists_every_difference_most_important_first()
    {
        Assert.Equal("game version · 1 missing · 2 different · 3 extra · load order · 4 DLC · 5 base files",
            SessionView.Short(new DiffSummary(false, 5, 4, 1, 3, 2, 7)));
        Assert.Equal("matches", SessionView.Short(new DiffSummary(true, 0, 0, 0, 0, 0, 0)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1024, 1)]
    [InlineData(1025, 2)]
    [InlineData(31_000, 31)]
    [InlineData(int.MaxValue, 2_097_152)]
    public void Kb_rounds_up(int bytes, int kb)
    {
        Assert.Equal(kb, SessionView.Kb(bytes));
    }

    [Fact]
    public void StreamStatus_covers_off_waiting_sent_and_included()
    {
        Assert.Equal("Off", SessionView.StreamStatus(false, false, "2387.09.17", 1000));
        Assert.Equal("On · waiting for the next save", SessionView.StreamStatus(true, false, null, 0));
        Assert.Equal("On · last sent 2387.09.17 · 32 KB/player", SessionView.StreamStatus(true, false, "2387.09.17", 32_000));
        Assert.Equal("Included in live game", SessionView.StreamStatus(true, true, "2387.09.17", 32_000));
    }
}

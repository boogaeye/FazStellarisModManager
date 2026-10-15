using System.Globalization;
using System.Text;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Display helpers for the Session tab: the ready ring, the players' avatars and sub-lines, and the streaming status.</summary>
public static class SessionView
{
    /// <summary>Players by ring colour: Ready (green), Mismatch or Unreliable (red), Waiting or Busy (amber).</summary>
    public readonly record struct ReadyCounts(int Ready, int Problem, int Pending)
    {
        public int Total => Ready + Problem + Pending;
    }

    public static ReadyCounts Count(IEnumerable<PlayerInfo> players)
    {
        int ready = 0, problem = 0, pending = 0;
        foreach (var p in players)
        {
            switch (p.Status)
            {
                case PlayerStatus.Ready: ready++; break;
                case PlayerStatus.Mismatch or PlayerStatus.Unreliable: problem++; break;
                default: pending++; break;
            }
        }
        return new ReadyCounts(ready, problem, pending);
    }

    /// <summary>The ring's CSS background: a conic gradient with the green, red and amber shares in that order.</summary>
    public static string RingGradient(ReadyCounts c)
    {
        if (c.Total == 0) return "var(--border)";
        var ok = 100.0 * c.Ready / c.Total;
        var bad = 100.0 * (c.Ready + c.Problem) / c.Total;
        return string.Create(CultureInfo.InvariantCulture,
            $"conic-gradient(var(--ok) 0 {ok:0.##}%, var(--danger) {ok:0.##}% {bad:0.##}%, var(--warn) {bad:0.##}% 100%)");
    }

    /// <summary>A hue (0-359) for a player's avatar, the same for the same name on every machine and run.</summary>
    public static int AvatarHue(string? name)
    {
        var h = 0;
        foreach (var c in name ?? "") h = (h * 31 + c) % 360;
        return h;
    }

    /// <summary>The avatar letter: the name's first letter or digit, upper-cased; "?" when it has none.</summary>
    public static string Initial(string? name)
    {
        foreach (var r in (name ?? "").EnumerateRunes())
            if (Rune.IsLetterOrDigit(r)) return Rune.ToUpperInvariant(r).ToString();
        return "?";
    }

    /// <summary>The line under a player's name in the roster: their role, then what they are doing or how they compare.</summary>
    public static string SubLine(PlayerInfo p, bool you)
    {
        if (p.IsHost) return you ? "host · you" : "host";
        var text = p.Status switch
        {
            PlayerStatus.Ready => "Ready",
            PlayerStatus.Busy => p.Activity ?? "Busy…",
            PlayerStatus.Waiting => "Waiting for scan…",
            PlayerStatus.Mismatch => p.Summary is { } s ? Short(s) : "differs",
            _ => "check scan warnings",
        };
        return you ? "you · " + text : text;
    }

    /// <summary>A compact form of <see cref="DiffSummary.ToString"/>, most important first: "2 missing · 24 different".</summary>
    public static string Short(DiffSummary s)
    {
        var parts = new List<string>();
        if (!s.GameVersionMatches) parts.Add("game version");
        if (s.Missing > 0) parts.Add($"{s.Missing} missing");
        if (s.Different > 0) parts.Add($"{s.Different} different");
        if (s.Extra > 0) parts.Add($"{s.Extra} extra");
        if (s.OutOfOrder > 0) parts.Add("load order");
        if (s.DlcDiffs > 0) parts.Add($"{s.DlcDiffs} DLC");
        if (s.BaseFileDiffs > 0) parts.Add($"{s.BaseFileDiffs} base files");
        return parts.Count == 0 ? "matches" : string.Join(" · ", parts);
    }

    /// <summary>Bytes as whole kilobytes, rounded up (0 stays 0).</summary>
    public static int Kb(int bytes) => bytes <= 0 ? 0 : (int)((bytes + 1023L) / 1024);

    /// <summary>
    /// The status line of a streaming toggle. <paramref name="included"/>: technology while the live game is on, which includes it.
    /// <paramref name="lastSentDate"/> and <paramref name="lastSentBytes"/> describe the last save sent to players in the current mode.
    /// </summary>
    public static string StreamStatus(bool on, bool included, string? lastSentDate, int lastSentBytes)
    {
        if (included) return "Included in live game";
        if (!on) return "Off";
        return lastSentDate is null ? "On · waiting for the next save" : $"On · last sent {lastSentDate} · {Kb(lastSentBytes)} KB/player";
    }
}

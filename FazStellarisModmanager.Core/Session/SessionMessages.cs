using System.Text.Json.Serialization;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Session;

/// <summary>Everything sent over a session connection. Serialized as JSON with a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Reject), "reject")]
[JsonDerivedType(typeof(SnapshotUpdate), "snapshot")]
[JsonDerivedType(typeof(HostTargetUpdate), "hostTarget")]
[JsonDerivedType(typeof(ClientBusy), "busy")]
[JsonDerivedType(typeof(Roster), "roster")]
[JsonDerivedType(typeof(Bye), "bye")]
[JsonDerivedType(typeof(LiveUpdate), "live")]
[JsonDerivedType(typeof(LiveViewAs), "liveViewAs")]
[JsonDerivedType(typeof(StreamingState), "streaming")]
public abstract record SessionMessage;

/// <summary>Client to host, first message.</summary>
public sealed record Hello(int ProtocolVersion, string AppVersion, string PlayerName) : SessionMessage;

/// <summary>
/// Host to client after a valid Hello: the client's id plus the host's list, snapshot and addon settings (the target to match).
/// Addons: the settings the host builds Faz Addons from, null when the host has addons off.
/// </summary>
public sealed record Welcome(string PlayerId, ModList HostList, MachineSnapshot HostSnapshot, AddonSettings? Addons) : SessionMessage;

/// <summary>Host to client instead of Welcome; the connection is closed afterwards.</summary>
public sealed record Reject(string Reason) : SessionMessage;

/// <summary>Client to host: my current setup.</summary>
public sealed record SnapshotUpdate(MachineSnapshot Snapshot) : SessionMessage;

/// <summary>Host to clients: the host rescanned or changed its list. Addons as in <see cref="Welcome"/>.</summary>
public sealed record HostTargetUpdate(ModList HostList, MachineSnapshot HostSnapshot, AddonSettings? Addons) : SessionMessage;

/// <summary>Client to host: I'm busy (e.g. matching the host); a SnapshotUpdate follows.</summary>
public sealed record ClientBusy(string Activity) : SessionMessage;

/// <summary>Host to clients: everyone in the session, host first.</summary>
public sealed record Roster(List<PlayerInfo> Players) : SessionMessage;

/// <summary>Either side: closing the connection on purpose.</summary>
public sealed record Bye(string? Reason) : SessionMessage;

/// <summary>Host to client: the latest save as this client may see it (see LiveFilter). ViewerId null: the host doesn't know the client's country yet (only Players are filled).</summary>
public sealed record LiveUpdate(FazStellarisModmanager.Core.Saves.GameSnapshot Snapshot, int? ViewerId) : SessionMessage;

/// <summary>Client to host: I play this country in the save.</summary>
public sealed record LiveViewAs(int CountryId) : SessionMessage;

/// <summary>Host to clients, on joining and whenever it changes: what the host streams (effective values, see <see cref="StreamModes.StateFor"/>).</summary>
public sealed record StreamingState(bool LiveGame, bool Technology) : SessionMessage;

/// <summary>What the host streams from its saves: nothing, each player's own techs, or the live game (which includes the techs).</summary>
public enum StreamMode { Off, Technology, LiveGame }

public static class StreamModes
{
    /// <summary>The effective mode of the host's two toggles: live game wins, then technology, else off.</summary>
    public static StreamMode For(bool liveGame, bool technology) =>
        liveGame ? StreamMode.LiveGame : technology ? StreamMode.Technology : StreamMode.Off;

    public static StreamMode For(AppSettings settings) => For(settings.StreamLiveGame, settings.StreamTechnology);

    /// <summary>The toggles as players see them: technology is on whenever the live game is.</summary>
    public static StreamingState StateFor(StreamMode mode) => new(mode == StreamMode.LiveGame, mode != StreamMode.Off);
}

public enum PlayerStatus { Waiting, Busy, Ready, Mismatch, Unreliable }

/// <summary>One roster row. Summary compares the player with the host (null for the host and before the first snapshot).</summary>
public sealed record PlayerInfo(string Id, string Name, bool IsHost, PlayerStatus Status, DiffSummary? Summary, string? Activity);

/// <summary>Counts from a <see cref="DiffResult"/>, small enough to broadcast to everyone.</summary>
public sealed record DiffSummary(bool GameVersionMatches, int BaseFileDiffs, int DlcDiffs, int Missing, int Extra, int Different, int OutOfOrder)
{
    public static DiffSummary From(DiffResult d) => new(
        d.GameVersionMatches,
        d.BaseFiles.Changed.Count + d.BaseFiles.OnlyInTarget.Count + d.BaseFiles.OnlyInMine.Count,
        d.Dlcs.Count(x => x.Status != UnitStatus.Ok),
        d.Mods.Count(x => x.Status == UnitStatus.Missing),
        d.Mods.Count(x => x.Status == UnitStatus.Extra),
        d.Mods.Count(x => x.Status == UnitStatus.ContentMismatch),
        d.Mods.Count(x => x.OutOfOrder));

    public override string ToString()
    {
        var parts = new List<string>();
        if (!GameVersionMatches) parts.Add("game version differs");
        if (BaseFileDiffs > 0) parts.Add($"{BaseFileDiffs} base game file(s) differ");
        if (DlcDiffs > 0) parts.Add($"{DlcDiffs} DLC difference(s)");
        if (Missing > 0) parts.Add($"{Missing} missing");
        if (Different > 0) parts.Add($"{Different} different");
        if (Extra > 0) parts.Add($"{Extra} extra");
        if (OutOfOrder > 0) parts.Add("load order differs");
        return parts.Count == 0 ? "matches" : string.Join(", ", parts);
    }
}

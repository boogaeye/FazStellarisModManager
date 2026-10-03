namespace FazStellarisModmanager.Core.Snapshots;

/// <summary>A file inside a content unit: relative path and MD5 (or "crc32:xxxxxxxx" for DLC zip entries).</summary>
public sealed record ModFile(string Path, string Md5, long Size);

/// <summary>One unit of content (base game, a DLC, or a mod) as seen on a machine.</summary>
public sealed record ModSnapshot(
    string Key,              // "base", "dlc:dlc001", "ugc:123", "local:foo.mod"
    string Name,
    string Descriptor,       // e.g. mod/ugc_123.mod or dlc/dlc001_x/dlc001.dlc
    string? RemoteId,
    string? Version,
    string? SupportedVersion,
    string ContentPath,
    int LoadOrder,
    List<ModFile> Files);

/// <summary>Everything one machine reports about its game setup.</summary>
public sealed record MachineSnapshot(
    string Machine,
    string GameVersion,
    string GameDir,
    DateTime TakenUtc,
    ModSnapshot Base,
    List<ModSnapshot> Dlcs,
    List<ModSnapshot> Mods);

namespace FazStellarisModmanager.Core.Library;

public enum ModSource { Workshop, Local }

/// <summary>A mod with a descriptor in Documents\...\Stellaris\mod. DescriptorRel is e.g. "mod/ugc_123.mod".</summary>
public sealed record InstalledMod(
    string Key,
    string Name,
    string DescriptorRel,
    string? RemoteId,
    string? Version,
    string? SupportedVersion,
    string ContentPath,
    ModSource Source,
    IReadOnlyList<string> Tags);

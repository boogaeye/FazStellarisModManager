namespace FazStellarisModmanager.Core.Library;

public enum ModSource { Workshop, Local }

/// <summary>A mod with a descriptor in Documents\...\Stellaris\mod. DescriptorRel is e.g. "mod/ugc_123.mod".</summary>
/// <param name="Picture">descriptor picture=, relative to the content</param>
public sealed record InstalledMod(
    string Key,
    string Name,
    string DescriptorRel,
    string? RemoteId,
    string? Version,
    string? SupportedVersion,
    string ContentPath,
    ModSource Source,
    IReadOnlyList<string> Tags,
    string? Picture = null);

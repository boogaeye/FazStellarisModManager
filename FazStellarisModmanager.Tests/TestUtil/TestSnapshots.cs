using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>Small hand-made snapshots and lists for session/diff tests.</summary>
public static class TestSnapshots
{
    public static MachineSnapshot Machine(string name, params string[] modKeys) =>
        new(name, "v4.4", "", DateTime.UtcNow,
            new ModSnapshot("base", "Stellaris", "checksum_manifest.txt", null, null, null, "", 0, [new ModFile("common/a.txt", "aa", 1)]),
            [],
            modKeys.Select((k, i) => new ModSnapshot(k, k, Rel(k), null, null, null, "", i + 1, [new ModFile("f.txt", "11", 1)])).ToList());

    public static ModList List(params string[] modKeys) =>
        new("Host list", modKeys.Select(k => new ModListEntry(k, k, Rel(k), null)).ToList(), []);

    /// <summary>"ugc:1" -> "mod/ugc_1.mod", matching ModKeys.</summary>
    public static string Rel(string key) => "mod/" + key.Replace(':', '_') + ".mod";
}

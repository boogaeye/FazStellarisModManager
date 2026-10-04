using FazStellarisModmanager.Core.Technology;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ResearchStateStoreTests
{
    [Fact]
    public void Round_trips_sorted_and_deduplicated_keys_per_label()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));

        store.Save("My list", new ResearchState(["b", "a", "A", " "], ["t"]));
        store.Save("Other", new ResearchState(["x"], []));

        var mine = store.Load("My list");
        Assert.Equal(["a", "b"], mine.Researched);
        Assert.Equal(["t"], mine.Targets);
        Assert.Equal(["x"], store.Load("Other").Researched);
        Assert.Same(ResearchState.Empty, store.Load("Never saved"));
    }

    [Fact]
    public void File_names_are_safe()
    {
        var store = new ResearchStateStore(Path.Combine("C:", "r"));

        Assert.Equal("a_b.json", Path.GetFileName(store.PathFor("a:b")));
        Assert.Equal("Current game (dlc_load.json).json", Path.GetFileName(store.PathFor("Current game (dlc_load.json)")));
        Assert.Equal("_CON.json", Path.GetFileName(store.PathFor("CON")));
        Assert.Equal("_.json", Path.GetFileName(store.PathFor("  ")));
    }

    [Fact]
    public void A_corrupt_file_gives_empty_state_and_a_warning()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathFor("Bad"), "{ not json");
        File.WriteAllText(store.PathFor("Blank"), "{}");
        var warnings = new List<string>();

        Assert.Same(ResearchState.Empty, store.Load("Bad", warnings));
        Assert.Single(warnings);
        var blank = store.Load("Blank", warnings);
        Assert.Empty(blank.Researched);
        Assert.Empty(blank.Targets);
        Assert.Single(warnings);
    }
}

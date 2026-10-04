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

    [Fact]
    public void A_corrupt_file_is_backed_up_next_to_it()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));
        Directory.CreateDirectory(store.Directory);
        var path = store.PathFor("Bad");
        File.WriteAllText(path, "{ not json");
        var warnings = new List<string>();

        Assert.Same(ResearchState.Empty, store.Load("Bad", warnings));

        Assert.Equal("{ not json", File.ReadAllText(path + ".bad"));
        Assert.Contains(".bad", Assert.Single(warnings));
    }

    [Fact]
    public void An_array_root_gives_empty_state_and_a_warning()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathFor("Arr"), "[]");
        var warnings = new List<string>();

        Assert.Same(ResearchState.Empty, store.Load("Arr", warnings));
        Assert.Single(warnings);
    }

    [Fact]
    public void Targets_are_sorted_and_deduplicated_on_save()
    {
        using var tmp = new TempDir();
        var store = new ResearchStateStore(Path.Combine(tmp.Path, "research"));

        store.Save("L", new ResearchState([], ["z", "b", "B", " ", "a"]));

        Assert.Equal(["a", "b", "z"], store.Load("L").Targets);
    }
}

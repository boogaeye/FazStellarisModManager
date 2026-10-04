using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class ModMatcherTests
{
    static ModIdentity Id(string key, string? workshop = null, string name = "", string? fingerprint = null) =>
        new(key, workshop, ModMatcher.NormalizeName(name), fingerprint);

    static List<(string Target, string Mine, MatchKind Kind)> Pair(ModIdentity[] targets, ModIdentity[] mine, MatchKind[]? rules = null) =>
        ModMatcher.Pair(targets, mine, x => x, x => x, rules).Select(p => (p.Target.Key, p.Mine.Key, p.Kind)).ToList();

    [Fact]
    public void Rules_apply_in_order_and_each_mod_pairs_once()
    {
        var targets = new[]
        {
            Id("ugc:1", "1", "One"),
            Id("ugc:2", "2", "Two"),
            Id("local:a.mod", null, "(Coll) Ethics  Fix"),
            Id("local:b.mod", null, "B", "FP"),
            Id("ugc:9", "9", "Nine"),
        };
        var mine = new[]
        {
            Id("ugc:1", "1", "One"),
            Id("local:x.mod", "2", "(Coll) Two"),
            Id("local:y.mod", "2", "Two again"),
            Id("local:e.mod", null, "ethics fix"),
            Id("local:renamed.mod", null, "Other", "FP"),
        };

        Assert.Equal(
            [("ugc:1", "ugc:1", MatchKind.Key), ("ugc:2", "local:x.mod", MatchKind.WorkshopId),
             ("local:a.mod", "local:e.mod", MatchKind.Name), ("local:b.mod", "local:renamed.mod", MatchKind.Files)],
            Pair(targets, mine));
    }

    [Fact]
    public void The_key_rule_wins_and_names_only_pair_mods_without_a_workshop_id()
    {
        Assert.Equal([("ugc:1", "ugc:1", MatchKind.Key)],
            Pair([Id("ugc:1", "1", "A")], [Id("local:copy.mod", "1", "A"), Id("ugc:1", "1", "A")]));
        Assert.Empty(Pair([Id("local:a.mod", "5", "Same")], [Id("local:b.mod", null, "Same")]));
        Assert.Empty(Pair([Id("local:a.mod", null, "Same")], [Id("local:b.mod", null, "Same")], [MatchKind.Key]));
    }

    [Theory]
    [InlineData("(Fazverse 4.4 with Flamer) DarkSpace", "darkspace")]
    [InlineData("  Ethics   Fix ", "ethics fix")]
    [InlineData("(a) (b) Name", "(b) name")]
    [InlineData("", "")]
    public void Names_lose_one_leading_collection_prefix_case_and_extra_spaces(string name, string expected) =>
        Assert.Equal(expected, ModMatcher.NormalizeName(name));

    [Fact]
    public void Workshop_ids_come_from_ugc_keys_or_remote_file_ids()
    {
        Assert.Equal("123", ModMatcher.WorkshopIdOf("ugc:123", null));
        Assert.Equal("123", ModMatcher.WorkshopIdOf("ugc:123", "999"));
        Assert.Equal("456", ModMatcher.WorkshopIdOf("local:x.mod", " 456 "));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", "0"));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", "abc"));
        Assert.Null(ModMatcher.WorkshopIdOf("local:x.mod", null));
    }

    [Fact]
    public void Fingerprints_ignore_file_order_and_case()
    {
        var a = ModMatcher.Fingerprint([new ModFile("common/A.txt", "AA", 1), new ModFile("b.txt", "bb", 1)]);
        var b = ModMatcher.Fingerprint([new ModFile("b.txt", "BB", 2), new ModFile("common/a.txt", "aa", 1)]);
        var c = ModMatcher.Fingerprint([new ModFile("common/a.txt", "ab", 1), new ModFile("b.txt", "bb", 1)]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Null(ModMatcher.Fingerprint([]));
    }

    [Fact]
    public void Fingerprints_tolerate_files_without_a_path_or_hash()
    {
        // Snapshots come from peers over the network, so fields declared non-null can still arrive as null.
        Assert.NotNull(ModMatcher.Fingerprint([new ModFile(null!, null!, 1), new ModFile("a.txt", "aa", 1)]));
    }

    [Fact]
    public void Fingerprints_are_computed_only_for_mods_still_unpaired_at_the_files_rule()
    {
        var calls = new List<string>();
        ModIdentity Lazy(string key, string? workshop, string name, string fp) =>
            new(key, workshop, ModMatcher.NormalizeName(name), () => { calls.Add(key); return fp; });

        // Everything pairs by key, Workshop id or an unambiguous name: no fingerprint is needed.
        Pair([Lazy("ugc:1", "1", "One", "A"), Lazy("local:a.mod", "2", "Two", "B"), Lazy("local:n.mod", null, "Name", "C")],
            [Lazy("ugc:1", "1", "One", "A"), Lazy("local:b.mod", "2", "Two", "B"), Lazy("local:m.mod", null, "(X) Name", "D")]);
        Assert.Empty(calls);

        // Nothing left on my side: the files rule has nothing to pair with.
        Pair([Lazy("ugc:1", "1", "One", "A"), Lazy("ugc:2", "2", "Two", "B")], [Lazy("ugc:1", "1", "One", "A")]);
        Assert.Empty(calls);

        // Only the unpaired mods are fingerprinted, each once.
        Assert.Equal([("ugc:1", "ugc:1", MatchKind.Key), ("local:x.mod", "local:y.mod", MatchKind.Files)],
            Pair([Lazy("ugc:1", "1", "One", "A"), Lazy("local:x.mod", null, "X", "F")],
                [Lazy("ugc:1", "1", "One", "A"), Lazy("local:y.mod", null, "Y", "F")]));
        Assert.Equal(["local:y.mod", "local:x.mod"], calls);
    }

    [Fact]
    public void Snapshot_identities_are_cached_per_instance()
    {
        var snap = new ModSnapshot("local:a.mod", "A", "mod/a.mod", null, null, null, "", 1, [new ModFile("f", "1", 1)]);

        Assert.Same(ModIdentity.Of(snap), ModIdentity.Of(snap));
        Assert.Equal(ModMatcher.Fingerprint(snap.Files), ModIdentity.Of(snap).Fingerprint);
    }

    [Fact]
    public void A_shared_name_prefers_the_mod_with_the_same_files()
    {
        var calls = 0;
        ModIdentity Lazy(string key, string name, string fp) => new(key, null, ModMatcher.NormalizeName(name), () => { calls++; return fp; });

        Assert.Equal([("local:h.mod", "local:new.mod", MatchKind.Name)],
            Pair([Lazy("local:h.mod", "Ethics Fix", "FP")], [Lazy("local:old.mod", "Ethics Fix", "OLD"), Lazy("local:new.mod", "(Coll) Ethics Fix", "FP")]));
        Assert.Equal(3, calls);

        // No identical one: the first still wins.
        Assert.Equal([("local:h.mod", "local:old.mod", MatchKind.Name)],
            Pair([Id("local:h.mod", null, "Ethics Fix", "FP")], [Id("local:old.mod", null, "Ethics Fix", "OLD"), Id("local:new.mod", null, "Ethics Fix")]));
    }
}

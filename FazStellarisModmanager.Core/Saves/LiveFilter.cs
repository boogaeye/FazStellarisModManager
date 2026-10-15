namespace FazStellarisModmanager.Core.Saves;

/// <summary>What one player may see of a save: their own country in full, contacted countries without techs and contacts, uncontacted ones as rank only.</summary>
public static class LiveFilter
{
    public static GameSnapshot For(GameSnapshot snapshot, int? viewerId)
    {
        var file = FileName(snapshot);
        if (viewerId is not int viewer) return snapshot with { SavePath = file, Countries = [], Community = null, Megastructures = null, Diplo = null };

        var me = snapshot.Countries.FirstOrDefault(c => c.Id == viewer);
        var contacted = me?.ContactedIds.ToHashSet() ?? [];
        var countries = snapshot.Countries.Select(c =>
            c.Id == viewer ? c
            : contacted.Contains(c.Id) ? c with { ContactedIds = [], Techs = [], Holdings = null, Roster = null, Flags = null, Ethics = null }
            : new SaveCountry(c.Id, c.Type, null, new Dictionary<string, string>(), null, null, c.VictoryRank, 0, 0, 0, 0, 0, 0, 0, null, [], []))
            .ToList();
        var diplo = snapshot.Diplo?.GetValueOrDefault(viewer) is { } mine
            ? new Dictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown> { [viewer] = mine }
            : null;
        return snapshot with { SavePath = file, Countries = countries, Community = null, Megastructures = null, Diplo = diplo };
    }

    /// <summary>
    /// The technology-only view of a full or tech-only snapshot: the players, plus the viewer's country with only its id,
    /// type and techs (none when the viewer is unknown). No other countries, community, megastructures, diplomacy or global flags.
    /// </summary>
    public static GameSnapshot TechOnly(GameSnapshot snapshot, int? viewerId)
    {
        var me = viewerId is int viewer ? snapshot.Countries.FirstOrDefault(c => c.Id == viewer) : null;
        IReadOnlyList<SaveCountry> countries = me is null ? []
            : [new SaveCountry(me.Id, me.Type, null, new Dictionary<string, string>(), null, null, 0, 0, 0, 0, 0, 0, 0, 0, null, [], me.Techs)];
        return snapshot with
        {
            SavePath = FileName(snapshot),
            TechOnly = true,
            Countries = countries,
            Community = null,
            Megastructures = null,
            Diplo = null,
            GlobalFlags = null,
        };
    }

    static string FileName(GameSnapshot snapshot) => Path.GetFileName(snapshot.SavePath.Replace((char)92, '/'));
}

namespace FazStellarisModmanager.Core.Saves;

/// <summary>What one player may see of a save: their own country in full, contacted countries without techs and contacts, uncontacted ones as rank only.</summary>
public static class LiveFilter
{
    public static GameSnapshot For(GameSnapshot snapshot, int? viewerId)
    {
        var file = Path.GetFileName(snapshot.SavePath.Replace((char)92, '/'));
        if (viewerId is not int viewer) return snapshot with { SavePath = file, Countries = [], Community = null, Megastructures = null, Diplo = null };

        var me = snapshot.Countries.FirstOrDefault(c => c.Id == viewer);
        var contacted = me?.ContactedIds.ToHashSet() ?? [];
        var countries = snapshot.Countries.Select(c =>
            c.Id == viewer ? c
            : contacted.Contains(c.Id) ? c with { ContactedIds = [], Techs = [], Holdings = null }
            : new SaveCountry(c.Id, c.Type, null, new Dictionary<string, string>(), null, null, c.VictoryRank, 0, 0, 0, 0, 0, 0, 0, null, [], []))
            .ToList();
        var diplo = snapshot.Diplo?.GetValueOrDefault(viewer) is { } mine
            ? new Dictionary<int, FazStellarisModmanager.Core.Diplomacy.DiploBreakdown> { [viewer] = mine }
            : null;
        return snapshot with { SavePath = file, Countries = countries, Community = null, Megastructures = null, Diplo = diplo };
    }
}

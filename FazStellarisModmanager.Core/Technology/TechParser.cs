using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A tech block's raw fields, before overrides, variables and localisation are applied.</summary>
public sealed record TechDefinition(
    string Key,
    string? Area,
    string? Tier,
    string? Category,
    string? Cost,
    IReadOnlyList<string> Prerequisites,
    bool IsStart,
    bool IsRare,
    bool IsDangerous,
    bool IsRepeatable,
    string? Icon,
    IReadOnlyList<string> Dlcs,
    PdxBlock Block);

public static class TechParser
{
    /// <summary>Every top-level block in a common/technology file except @variables.</summary>
    public static List<TechDefinition> Parse(string text)
    {
        var root = ParadoxScriptParser.Parse(text);
        var result = new List<TechDefinition>();
        foreach (var entry in root.Entries)
        {
            if (entry.Value is not PdxBlock b || entry.Key.StartsWith('@')) continue;
            var levels = b.GetString("levels");
            result.Add(new TechDefinition(
                entry.Key,
                b.GetString("area"),
                b.GetString("tier"),
                b.GetBlock("category")?.StringItems.FirstOrDefault() ?? b.GetString("category"),
                b.GetString("cost"),
                b.GetBlock("prerequisites")?.StringItems.ToList() ?? [],
                Yes(b, "start_tech"),
                Yes(b, "is_rare"),
                Yes(b, "is_dangerous"),
                levels is not null && levels != "1" && levels != "0",
                b.GetString("icon"),
                FindDlcs(b.GetBlock("potential")).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                b));
        }
        return result;
    }

    static bool Yes(PdxBlock b, string key) => string.Equals(b.GetString(key), "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>DLCs a tech's potential block requires. NOT/NOR/NAND are not searched: they exclude a DLC rather than require it.</summary>
    static IEnumerable<string> FindDlcs(PdxBlock? block)
    {
        if (block is null) yield break;
        foreach (var e in block.Entries)
        {
            if (e.Value is string s)
            {
                if (e.Key.Equals("host_has_dlc", StringComparison.OrdinalIgnoreCase) || e.Key.Equals("has_dlc", StringComparison.OrdinalIgnoreCase))
                    yield return s;
            }
            else if (e.Value is PdxBlock child && !IsNegation(e.Key))
                foreach (var d in FindDlcs(child)) yield return d;
        }
        foreach (var item in block.Items)
            if (item is PdxBlock child)
                foreach (var d in FindDlcs(child)) yield return d;
    }

    static bool IsNegation(string key) =>
        key.Equals("NOT", StringComparison.OrdinalIgnoreCase) || key.Equals("NOR", StringComparison.OrdinalIgnoreCase) || key.Equals("NAND", StringComparison.OrdinalIgnoreCase);
}

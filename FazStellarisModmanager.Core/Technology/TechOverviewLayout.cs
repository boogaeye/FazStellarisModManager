using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A tech's box on the overview map: top-left corner (X, Y), row within its band × column cell.</summary>
public sealed record OverviewNode(string Key, TechArea Area, int ColumnIndex, int Row, double X, double Y);

/// <summary>Prerequisite From -> tech To. Forward = To's column is right of From's (otherwise draw an arc over the top).</summary>
public sealed record OverviewEdge(string From, string To, bool Forward);

public sealed record OverviewBand(TechArea Area, string Label, double Y, double Height);

public sealed record OverviewColumn(string Label, double X);

public sealed record TechOverview(
    IReadOnlyList<OverviewNode> Nodes,
    IReadOnlyList<OverviewEdge> Edges,
    IReadOnlyList<OverviewBand> Bands,
    IReadOnlyList<OverviewColumn> Columns,
    double Width,
    double Height)
{
    Dictionary<string, OverviewNode>? _byKey;

    public OverviewNode? Find(string key) =>
        (_byKey ??= Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase)).GetValueOrDefault(key);
}

/// <summary>
/// The whole tree as one map: a band per area (Physics, Society, Engineering, then Other if used), a column per used tier, then
/// Repeatable and "?" (no tier) when present. Inside a cell: category (none last), then the average position of prerequisites already
/// placed further left, then name.
/// </summary>
public static class TechOverviewLayout
{
    public const double NodeWidth = 168, NodeHeight = 34, ColumnGap = 72, RowPitch = 42, BandGap = 36, LabelGutter = 96, HeaderHeight = 28;
    public const double ColumnWidth = NodeWidth + ColumnGap;

    // Rows of different bands compare on one scale: band index first, then row.
    const int BandRowScale = 100_000;

    static readonly TechArea[] AreaOrder = [TechArea.Physics, TechArea.Society, TechArea.Engineering, TechArea.Other];

    public static TechOverview Build(TechDatabase db)
    {
        var techs = db.Techs.Values.ToList();

        var tiers = techs.Where(t => !t.IsRepeatable && t.Tier is not null).Select(t => t.Tier!.Value).Distinct().Order().ToList();
        var labels = tiers.Select(t => "Tier " + t.ToString(CultureInfo.InvariantCulture)).ToList();
        var repeatableColumn = -1;
        var unknownColumn = -1;
        if (techs.Any(t => t.IsRepeatable)) { repeatableColumn = labels.Count; labels.Add("Repeatable"); }
        if (techs.Any(t => !t.IsRepeatable && t.Tier is null)) { unknownColumn = labels.Count; labels.Add("?"); }
        int ColumnOf(Tech t) => t.IsRepeatable ? repeatableColumn : t.Tier is { } tier ? tiers.BinarySearch(tier) : unknownColumn;

        var areas = AreaOrder.Where(a => a != TechArea.Other || techs.Any(t => t.Area == TechArea.Other)).ToList();
        int BandOf(Tech t) => areas.IndexOf(t.Area);

        var cells = new Dictionary<(int Band, int Column), List<Tech>>();
        foreach (var t in techs)
        {
            var cell = (BandOf(t), ColumnOf(t));
            if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = [];
            list.Add(t);
        }

        var row = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var column = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < labels.Count; c++)
            for (var band = 0; band < areas.Count; band++)
            {
                if (!cells.TryGetValue((band, c), out var members)) continue;
                var current = c;
                double Barycenter(Tech t)
                {
                    var rows = t.Prerequisites
                        .Select(p => db.Techs.TryGetValue(p, out var pre) ? pre : null)
                        .OfType<Tech>()
                        .Where(pre => column.TryGetValue(pre.Key, out var pc) && pc < current)
                        .Select(pre => (double)BandOf(pre) * BandRowScale + row[pre.Key])
                        .ToList();
                    return rows.Count == 0 ? double.MaxValue : rows.Average();
                }
                var ordered = members
                    .OrderBy(t => t.Category is null ? 1 : 0)
                    .ThenBy(t => t.Category ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(Barycenter)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Key, StringComparer.Ordinal)
                    .ToList();
                cells[(band, c)] = ordered;
                for (var i = 0; i < ordered.Count; i++)
                {
                    row[ordered[i].Key] = i;
                    column[ordered[i].Key] = c;
                }
            }

        var bands = new List<OverviewBand>();
        var y = HeaderHeight;
        for (var band = 0; band < areas.Count; band++)
        {
            var rows = Math.Max(1, cells.Where(kv => kv.Key.Band == band).Select(kv => kv.Value.Count).DefaultIfEmpty(0).Max());
            bands.Add(new OverviewBand(areas[band], AreaLabel(areas[band]), y, rows * RowPitch));
            y += rows * RowPitch + BandGap;
        }
        var columns = labels.Select((label, i) => new OverviewColumn(label, LabelGutter + i * ColumnWidth)).ToList();

        var nodes = new List<OverviewNode>();
        foreach (var ((band, c), members) in cells.OrderBy(kv => kv.Key.Column).ThenBy(kv => kv.Key.Band))
            for (var i = 0; i < members.Count; i++)
                nodes.Add(new OverviewNode(members[i].Key, areas[band], c, i, columns[c].X, bands[band].Y + i * RowPitch));

        var edges = new List<OverviewEdge>();
        foreach (var t in techs)
            foreach (var p in t.Prerequisites.Distinct(StringComparer.OrdinalIgnoreCase))
                if (db.Techs.TryGetValue(p, out var pre))
                    edges.Add(new OverviewEdge(pre.Key, t.Key, column[t.Key] > column[pre.Key]));

        return new TechOverview(nodes, edges, bands, columns, LabelGutter + labels.Count * ColumnWidth, bands[^1].Y + bands[^1].Height);
    }

    static string AreaLabel(TechArea area) => area switch
    {
        TechArea.Physics => "Physics",
        TechArea.Society => "Society",
        TechArea.Engineering => "Engineering",
        _ => "Other",
    };
}

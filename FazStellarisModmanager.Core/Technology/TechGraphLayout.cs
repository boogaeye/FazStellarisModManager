namespace FazStellarisModmanager.Core.Technology;

/// <summary>A node in the focus graph. Column 0 is the focus, negative columns are prerequisites, positive are dependents. Row is the index within its column.</summary>
public sealed record GraphNode(string Key, int Column, int Row, bool IsFocus);

/// <summary>From = prerequisite, To = the tech that requires it.</summary>
public sealed record GraphEdge(string From, string To);

/// <summary>Nodes left out of a crowded column.</summary>
public sealed record GraphMore(int Column, int Hidden);

public sealed record TechGraph(string Focus, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphMore> More, int MinColumn, int MaxColumn, int MaxRows);

public static class TechGraphLayout
{
    public const int MaxPerColumn = 25;

    /// <summary>
    /// Builds the focus graph: prerequisites in negative columns, dependents in positive columns, <paramref name="depth"/> clamped to 1..3.
    /// Edges are drawn only from a prerequisite in a column further left to a dependent further right. Links between techs in the same
    /// column, or pointing toward the focus, are not drawn (the focus view shows the shortest-distance placement). Edges may span more
    /// than one column (e.g. from -2 to +1), so renderers should route long edges around the nodes.
    /// Columns are capped at <see cref="MaxPerColumn"/>; members with no shown neighbour in the nearer column are dropped, and
    /// everything hidden for either reason is counted in a single <see cref="GraphMore"/> per column.
    /// </summary>
    public static TechGraph Build(TechDatabase db, string focus, int depth)
    {
        if (!db.Techs.TryGetValue(focus, out var focusTech)) throw new ArgumentException($"Unknown technology '{focus}'.", nameof(focus));
        depth = Math.Clamp(depth, 1, 3);

        var column = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [focusTech.Key] = 0 };
        Expand(db, column, focusTech.Key, depth, -1, k => db.Techs[k].Prerequisites);
        Expand(db, column, focusTech.Key, depth, +1, k => db.Dependents(k));

        var centred = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [focusTech.Key] = 0 };
        var columns = new Dictionary<int, List<string>> { [0] = [focusTech.Key] };
        var more = new List<GraphMore>();
        foreach (var dir in new[] { -1, 1 })
        {
            for (int c = dir; Math.Abs(c) <= depth; c += dir)
            {
                var members = column.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList();
                if (members.Count == 0) break;
                var nearer = c - dir;

                double Barycenter(string key)
                {
                    var neighbours = dir > 0 ? db.Techs[key].Prerequisites : db.Dependents(key);
                    var rows = neighbours
                        .Where(n => db.Techs.TryGetValue(n, out var nt) && centred.ContainsKey(nt.Key) && column[nt.Key] == nearer)
                        .Select(n => centred[db.Techs[n].Key])
                        .ToList();
                    return rows.Count == 0 ? double.MaxValue : rows.Average();
                }

                var candidates = members.Where(k => Barycenter(k) != double.MaxValue).ToList();
                var ordered = candidates
                    .OrderBy(Barycenter)
                    .ThenBy(k => db.Techs[k].Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(k => k, StringComparer.Ordinal)
                    .ToList();
                if (ordered.Count > MaxPerColumn) ordered = ordered.Take(MaxPerColumn).ToList();
                var hiddenTotal = members.Count - ordered.Count;
                if (hiddenTotal > 0) more.Add(new GraphMore(c, hiddenTotal));
                if (ordered.Count == 0) break;
                for (int i = 0; i < ordered.Count; i++) centred[ordered[i]] = i - (ordered.Count - 1) / 2.0;
                columns[c] = ordered;
            }
        }

        var nodes = columns.OrderBy(kv => kv.Key)
            .SelectMany(kv => kv.Value.Select((key, row) => new GraphNode(key, kv.Key, row, kv.Key == 0)))
            .ToList();
        var columnOf = nodes.ToDictionary(n => n.Key, n => n.Column, StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();
        foreach (var n in nodes)
            foreach (var p in db.Techs[n.Key].Prerequisites)
                if (db.Techs.TryGetValue(p, out var pre) && columnOf.TryGetValue(pre.Key, out var pc) && pc < n.Column)
                    edges.Add(new GraphEdge(pre.Key, n.Key));

        return new TechGraph(focusTech.Key, nodes, edges, more, columns.Keys.Concat(more.Select(m => m.Column)).Min(), columns.Keys.Concat(more.Select(m => m.Column)).Max(), columns.Values.Max(l => l.Count));
    }

    static void Expand(TechDatabase db, Dictionary<string, int> column, string focus, int depth, int dir, Func<string, IEnumerable<string>> next)
    {
        var frontier = new List<string> { focus };
        for (int step = 1; step <= depth && frontier.Count > 0; step++)
        {
            var nextFrontier = new List<string>();
            foreach (var key in frontier)
                foreach (var raw in next(key))
                    if (db.Techs.TryGetValue(raw, out var t) && !column.ContainsKey(t.Key))
                    {
                        column[t.Key] = step * dir;
                        nextFrontier.Add(t.Key);
                    }
            frontier = nextFrontier;
        }
    }
}

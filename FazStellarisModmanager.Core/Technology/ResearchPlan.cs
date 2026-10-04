using System.Globalization;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>A prerequisite named by a tech but missing from the tree.</summary>
public sealed record MissingPrerequisite(string Tech, string Prerequisite);

/// <summary>What is left to research for some targets. <see cref="Steps"/> is a valid research order: every tech after its needed prerequisites.</summary>
public sealed record ResearchRoute(
    IReadOnlyList<string> Steps,
    double TotalCost,
    int UnknownCostCount,
    int SkippedResearched,
    int SkippedStarting,
    IReadOnlyList<string> UnknownTargets,
    IReadOnlyList<MissingPrerequisite> MissingPrerequisites,
    IReadOnlyList<string> Cycles)
{
    /// <summary>Every tech that could not be ordered: the members of prerequisite loops and anything that depends on them.</summary>
    public IReadOnlyList<string> Cycles { get; init; } = Cycles;

    public static ResearchRoute Empty { get; } = new([], 0, 0, 0, 0, [], [], []);

    HashSet<string>? _needed;
    IReadOnlyList<string>? _neededFor;

    /// <summary>The steps as a case-insensitive set, for highlighting. Rebuilt when <see cref="Steps"/> is replaced (a <c>with</c> copy).</summary>
    public IReadOnlySet<string> Needed
    {
        get
        {
            if (_needed is null || !ReferenceEquals(_neededFor, Steps))
            {
                _needed = new HashSet<string>(Steps, StringComparer.OrdinalIgnoreCase);
                _neededFor = Steps;
            }
            return _needed;
        }
    }
}

public static class ResearchPlan
{
    /// <summary>
    /// Everything the targets need (all prerequisites, transitively) that is not researched yet. Researched techs (the given ones and every
    /// starting tech) are skipped and not expanded. Ties among ready techs: tier (repeatable, then unknown, last), area, name, key.
    /// Techs that cannot be ordered (loop members and anything depending on them) are appended in the same order and listed in
    /// <see cref="ResearchRoute.Cycles"/>. Null or blank keys in <paramref name="targets"/> and <paramref name="researched"/> are ignored.
    /// </summary>
    public static ResearchRoute Build(TechDatabase db, IEnumerable<string> targets, IEnumerable<string> researched)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in researched)
            if (!string.IsNullOrWhiteSpace(r) && db.Techs.TryGetValue(r, out var t)) done.Add(t.Key);

        var unknownTargets = new List<string>();
        var stack = new Stack<string>();
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target)) continue;
            if (db.Techs.TryGetValue(target, out var t)) stack.Push(t.Key);
            else if (!unknownTargets.Contains(target, StringComparer.OrdinalIgnoreCase)) unknownTargets.Add(target);
        }

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<MissingPrerequisite>();
        int skippedResearched = 0, skippedStarting = 0;
        while (stack.Count > 0)
        {
            var key = stack.Pop();
            if (!visited.Add(key)) continue;
            var tech = db.Techs[key];
            if (done.Contains(key)) { skippedResearched++; continue; }
            if (tech.IsStart) { skippedStarting++; continue; }
            needed.Add(key);
            foreach (var p in tech.Prerequisites)
            {
                if (db.Techs.TryGetValue(p, out var pre)) stack.Push(pre.Key);
                else missing.Add(new MissingPrerequisite(key, p));
            }
        }

        var order = Comparer<string>.Create((a, b) => Compare(db.Techs[a], db.Techs[b]));
        var waiting = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in needed)
        {
            var prereqs = db.Techs[key].Prerequisites
                .Where(p => db.Techs.ContainsKey(p))
                .Select(p => db.Techs[p].Key)
                .Where(needed.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            waiting[key] = prereqs.Count;
            foreach (var p in prereqs)
            {
                if (!dependents.TryGetValue(p, out var list)) dependents[p] = list = [];
                list.Add(key);
            }
        }

        var ready = new SortedSet<string>(needed.Where(k => waiting[k] == 0), order);
        var steps = new List<string>(needed.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            steps.Add(next);
            if (dependents.TryGetValue(next, out var deps))
                foreach (var d in deps)
                    if (--waiting[d] == 0) ready.Add(d);
        }
        var cycles = needed.Where(k => waiting[k] > 0).Order(order).ToList();
        steps.AddRange(cycles);

        double total = 0;
        var unknownCost = 0;
        foreach (var key in steps)
        {
            if (double.TryParse(db.Techs[key].Cost, NumberStyles.Float, CultureInfo.InvariantCulture, out var cost) && double.IsFinite(cost)) total += cost;
            else unknownCost++;
        }
        var missingSorted = missing
            .DistinctBy(m => (m.Tech.ToUpperInvariant(), m.Prerequisite.ToUpperInvariant()))
            .OrderBy(m => m.Tech, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Prerequisite, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ResearchRoute(steps, total, unknownCost, skippedResearched, skippedStarting, unknownTargets, missingSorted, cycles);
    }

    /// <summary>Order of techs that are ready at the same time: tier (repeatable, then unknown, last), area, name, key.</summary>
    public static int Compare(Tech a, Tech b)
    {
        var c = TierRank(a).CompareTo(TierRank(b));
        if (c == 0) c = a.Area.CompareTo(b.Area);
        if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        if (c == 0) c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        return c;
    }

    static int TierRank(Tech t) => t.IsRepeatable ? int.MaxValue - 1 : t.Tier ?? int.MaxValue;
}

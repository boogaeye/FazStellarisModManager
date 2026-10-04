using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Technology;

/// <summary>One "swap_type" of a job: while <see cref="Trigger"/> holds, the job shows as job <see cref="Name"/> with icon job_<see cref="Icon"/>.</summary>
public sealed record JobSwap(string Name, string Icon, PdxBlock? Trigger);

/// <summary>Job renames from common/pop_jobs swappable_data (Bureaucrat -> Priest, Manager, …), keyed by job.</summary>
public sealed class JobSwaps
{
    readonly Dictionary<string, IReadOnlyList<JobSwap>> _swaps = new(StringComparer.OrdinalIgnoreCase);

    public static JobSwaps Empty { get; } = new();

    public int Count => _swaps.Count;

    /// <summary>The job's swaps in file order; empty for an unknown job or one without swaps.</summary>
    public IReadOnlyList<JobSwap> For(string job) => _swaps.TryGetValue(job, out var list) ? list : [];

    /// <summary>From job definitions (job key -> block), e.g. <see cref="CommonDefinitions.Load"/> of common/pop_jobs.</summary>
    public static JobSwaps From(IReadOnlyDictionary<string, PdxBlock> jobs)
    {
        var index = new JobSwaps();
        foreach (var (job, block) in jobs)
        {
            if (block.GetBlock("swappable_data") is not { } data) continue;
            var swaps = data.Entries
                .Where(e => e.Key.Equals("swap_type", StringComparison.OrdinalIgnoreCase) && e.Value is PdxBlock)
                .Select(e => (PdxBlock)e.Value)
                .Where(s => s.GetString("name") is { Length: > 0 })
                .Select(s => new JobSwap(s.GetString("name")!, s.GetString("icon") ?? s.GetString("name")!, s.GetBlock("trigger")))
                .ToList();
            if (swaps.Count > 0) index._swaps[job] = swaps;
        }
        return index;
    }
}

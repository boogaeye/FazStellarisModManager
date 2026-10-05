using System.Collections.Concurrent;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Core.Conflicts;

/// <summary>Result of one scan: each mod's script files (null mods give none) and lines for anything unreadable.</summary>
public sealed record DefinitionScan(IReadOnlyList<ModScripts> Mods, IReadOnlyList<string> Errors);

/// <summary>The last scan shown on the Conflicts tab, for the list at <see cref="ListVersion"/>.</summary>
public sealed record ConflictSnapshot(int ListVersion, IReadOnlyList<ModListEntry> Entries, IReadOnlyList<string> Errors,
    DefinitionReport Report, int Definitions, TimeSpan Took);

/// <summary>Reads the definitions of a list's mods (folders or zips), caching each file's names by path and stamp for the app run. Thread-safe.</summary>
public sealed class DefinitionScanService
{
    readonly ConcurrentDictionary<string, IReadOnlyList<string>> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last result the Conflicts tab showed (kept so returning to the tab doesn't rescan).</summary>
    public ConflictSnapshot? Last { get; set; }

    /// <summary>Scans the mods four at a time; <paramref name="onProgress"/> gets the number of mods done (from any thread).</summary>
    public async Task<DefinitionScan> ScanAsync(IReadOnlyList<InstalledMod?> mods, Action<int>? onProgress, CancellationToken ct)
    {
        var result = new ModScripts[mods.Count];
        var errors = new ConcurrentQueue<string>();
        var done = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, mods.Count), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, (i, token) =>
        {
            result[i] = ScanMod(mods[i], errors, token);
            onProgress?.Invoke(Interlocked.Increment(ref done));
            return ValueTask.CompletedTask;
        });
        return new DefinitionScan(result, errors.ToList());
    }

    ModScripts ScanMod(InstalledMod? mod, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        if (mod is null) return new ModScripts([]);
        ContentSource source;
        try
        {
            source = ContentSource.FromPath(mod.Name, mod.ContentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            errors.Enqueue($"{mod.Name}: {ex.Message}");
            return new ModScripts([]);
        }

        using (source)
        {
            var files = new List<ScriptFile>();
            foreach (var rel in source.Files("common", ".txt").Concat(source.Files("events", ".txt")))
            {
                ct.ThrowIfCancellationRequested();
                if (DefinitionRules.Classify(rel) is not { } c) continue;
                try
                {
                    var key = mod.ContentPath + "|" + rel + "|" + source.Stamp(rel);
                    var names = _names.GetOrAdd(key, _ => DefinitionScanner.Names(source.ReadText(rel), c.Kind));
                    files.Add(new ScriptFile(rel, names));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    errors.Enqueue($"{mod.Name}: {rel}: {ex.Message}");
                }
            }
            return new ModScripts(files);
        }
    }
}

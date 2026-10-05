using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Diplomacy;
using FazStellarisModmanager.Core.Saves;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Services;

/// <summary>
/// Adds diplomatic weight breakdowns to a snapshot. Loading the game and mod definitions is slow, so the context is
/// cached until the list of content paths changes. Call from a background thread only (LiveGameService does).
/// </summary>
public sealed class DiploEnricher(ModManagerService manager)
{
    readonly object _gate = new();
    string? _key;
    DiploContext? _context;

    public GameSnapshot Enrich(GameSnapshot s)
    {
        lock (_gate)
        {
            if (manager.Library.Count == 0) manager.RefreshLibrary();
            var paths = ContentPaths();
            var key = string.Join("|", paths.Select(p => p.Path));
            if (_context is null || key != _key)
            {
                _context = Load(paths);
                _key = key;
            }
            return _context.Enrich(s);
        }
    }

    List<(string Name, string Path, bool IsBase)> ContentPaths()
    {
        var list = new List<(string, string, bool)>();
        if (manager.Resolve().GameDir is { } g) list.Add(("game", g, true));
        var byRel = manager.Library.GroupBy(m => m.DescriptorRel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var e in manager.ImportCurrent("live").Mods)
            if (byRel.TryGetValue(e.DescriptorRel, out var mod)) list.Add((mod.Name, mod.ContentPath, false));
        return list;
    }

    static DiploContext Load(List<(string Name, string Path, bool IsBase)> paths)
    {
        var sources = new List<ContentSource>();
        try
        {
            foreach (var p in paths)
            {
                try { sources.Add(ContentSource.FromPath(p.Name, p.Path, p.IsBase)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            return DiploContext.Load(sources);
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
    }
}

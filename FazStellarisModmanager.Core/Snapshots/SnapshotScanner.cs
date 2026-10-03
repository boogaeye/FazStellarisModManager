using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Core.Snapshots;

public static class SnapshotScanner
{
    /// <summary>
    /// Snapshots the base game, enabled DLCs and the mods enabled in dlc_load.json (in load order).
    /// Problems are reported via <paramref name="progress"/> and recorded in <see cref="MachineSnapshot.Warnings"/>.
    /// <paramref name="progress"/> may be invoked concurrently from worker threads.
    /// </summary>
    public static async Task<MachineSnapshot> ScanAsync(string userDir, string gameDir, string machineName, HashCache cache,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var load = DlcLoadFile.Read(userDir);
        var warnings = new ConcurrentQueue<string>();
        void Warn(string m) { warnings.Enqueue(m); progress?.Report(m); }

        var manifestPath = Path.Combine(gameDir, "checksum_manifest.txt");
        var rules = File.Exists(manifestPath) ? Manifest.Parse(await File.ReadAllTextAsync(manifestPath, ct)) : [];
        progress?.Report($"Hashing base game: {gameDir}");
        var baseFiles = await HashAllAsync(FileCollector.Collect(rules, [gameDir]).Select(kv => (kv.Key, kv.Value)), cache, Warn, ct);
        var gameVersion = ReadGameVersion(userDir, Warn);
        var baseSnap = new ModSnapshot("base", "Stellaris", "checksum_manifest.txt", null, gameVersion, null, gameDir, 0, baseFiles);

        var dlcs = ScanDlcs(gameDir, load, Warn, progress, ct);

        var mods = new List<ModSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int order = 0;
        foreach (var raw in load.EnabledMods)
        {
            ct.ThrowIfCancellationRequested();
            var rel = raw.Replace('\\', '/');
            var key = ModKeys.For(rel);
            if (!seen.Add(key)) { Warn($"  [duplicate] {rel} skipped"); continue; }
            order++;

            try { mods.Add(await ScanModAsync(userDir, rel, key, order, cache, Warn, progress, ct)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                // One bad mod must not sink the scan; keep a placeholder so the unit still shows up in the diff.
                Warn($"  [{Kind(ex)}] {rel}: {ex.Message}");
                mods.Add(new ModSnapshot(key, Path.GetFileNameWithoutExtension(rel), rel, null, null, null, "", order, []));
            }
        }

        return new MachineSnapshot(machineName, gameVersion, gameDir, DateTime.UtcNow, baseSnap, dlcs, mods, warnings.ToList());
    }

    static string Kind(Exception ex) => ex is InvalidDataException ? "broken zip" : "unreadable";

    static async Task<ModSnapshot> ScanModAsync(string userDir, string rel, string key, int order, HashCache cache,
        Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        var descriptorPath = Path.Combine(userDir, rel);
        if (!File.Exists(descriptorPath))
        {
            warn($"  [missing descriptor] {rel}");
            return new ModSnapshot(key, Path.GetFileNameWithoutExtension(rel), rel, null, null, null, "", order, []);
        }

        var d = ModDescriptor.Load(descriptorPath);
        var name = d.Name ?? Path.GetFileNameWithoutExtension(rel);
        var content = ModLibrary.ResolveContent(userDir, d);
        List<ModFile> files;
        if (Directory.Exists(content))
        {
            files = await HashAllAsync(
                Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories)
                    .Select(f => (Path.GetRelativePath(content, f).Replace('\\', '/'), f)),
                cache, warn, ct);
        }
        else if (File.Exists(content) && content.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            files = HashZipEntries(content, ct);
        }
        else
        {
            warn($"  [missing content] {name} -> {content}");
            files = [];
        }

        progress?.Report($"  {order,3}. {name} ({files.Count} files)");
        return new ModSnapshot(key, name, rel, d.RemoteFileId, d.Version, d.SupportedVersion, content, order, files);
    }

    static async Task<List<ModFile>> HashAllAsync(IEnumerable<(string Rel, string Abs)> files, HashCache cache,
        Action<string> warn, CancellationToken ct)
    {
        var bag = new ConcurrentBag<ModFile>();
        await Parallel.ForEachAsync(files, ct, (f, _) =>
        {
            try
            {
                var md5 = cache.GetMd5(f.Abs, out var size);
                bag.Add(new ModFile(f.Rel, md5, size));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Files can vanish or be locked mid-scan (e.g. Steam updating a mod); skip and report.
                warn($"  [unreadable] {f.Rel}: {ex.Message}");
            }
            return ValueTask.CompletedTask;
        });
        return Sorted(bag);
    }

    static List<ModFile> HashZipEntries(string zipPath, CancellationToken ct)
    {
        var files = new List<ModFile>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.FullName.EndsWith('/')) continue;
            using var s = e.Open();
            files.Add(new ModFile(e.FullName.Replace('\\', '/'), Convert.ToHexStringLower(MD5.HashData(s)), e.Length));
        }
        return Sorted(files);
    }

    static List<ModSnapshot> ScanDlcs(string gameDir, DlcLoad load, Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        var disabled = new HashSet<string>(load.DisabledDlcs.Select(d => d.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        var dlcs = new List<ModSnapshot>();
        var dlcRoot = Path.Combine(gameDir, "dlc");
        if (!Directory.Exists(dlcRoot)) return dlcs;

        int order = 0;
        foreach (var descPath in Directory.EnumerateFiles(dlcRoot, "*.dlc", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var descRel = Path.GetRelativePath(gameDir, descPath).Replace('\\', '/');
            if (disabled.Contains(descRel)) { progress?.Report($"  [disabled] {descRel}"); continue; }

            var key = "dlc:" + Path.GetFileNameWithoutExtension(descPath);
            try { dlcs.Add(ScanDlc(gameDir, descPath, descRel, key, ++order, warn)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                warn($"  [{Kind(ex)}] {descRel}: {ex.Message}");
                dlcs.Add(new ModSnapshot(key, Path.GetFileNameWithoutExtension(descPath), descRel, null, null, null, "", order, []));
            }
        }
        return dlcs;
    }

    static ModSnapshot ScanDlc(string gameDir, string descPath, string descRel, string key, int order, Action<string> warn)
    {
        var b = ParadoxScriptParser.Parse(File.ReadAllText(descPath));
        var name = b.GetString("name") ?? Path.GetFileNameWithoutExtension(descPath);
        var archive = b.GetString("archive");
        var zipPath = archive is null ? null : Path.Combine(gameDir, archive);
        var files = new List<ModFile>();
        if (zipPath is not null && File.Exists(zipPath))
        {
            // DLC zips hold music/sound/art only (gameplay data ships in the base install),
            // so per-entry CRCs from the central directory are enough and cost no decompression.
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
                if (!e.FullName.EndsWith('/')) files.Add(new ModFile(e.FullName.Replace('\\', '/'), $"crc32:{e.Crc32:x8}", e.Length));
        }
        else warn($"  [missing archive] {name} -> {archive}");

        return new ModSnapshot(key, name, descRel, b.GetString("steam_id"), b.GetString("zip_checksum"), null, zipPath ?? "", order, Sorted(files));
    }

    static List<ModFile> Sorted(IEnumerable<ModFile> files)
    {
        var list = files.ToList();
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
        return list;
    }

    static string ReadGameVersion(string userDir, Action<string> warn)
    {
        var log = Path.Combine(userDir, "logs", "game.log");
        if (!File.Exists(log)) return "unknown";
        try
        {
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var m = Regex.Match(sr.ReadToEnd(), @"Game Version:\s*(.+)");
            return m.Success ? m.Groups[1].Value.Trim() : "unknown";
        }
        catch (IOException ex)
        {
            warn($"  [warning] could not read game.log: {ex.Message}");
            return "unknown";
        }
    }
}

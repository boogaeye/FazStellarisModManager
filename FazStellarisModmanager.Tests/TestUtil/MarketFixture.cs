using System.Text;
using FazStellarisModmanager.Core.Addons;
using FazStellarisModmanager.Core.Technology;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>
/// Synthetic game content for the market catalog and the addon builder: a base game folder plus mod folders (in the order added),
/// each opened as a <see cref="ContentSource"/>. Text is written exactly as given (no BOM) unless written as bytes.
/// </summary>
public sealed class MarketFixture : IDisposable
{
    public static readonly string Lf = ((char)10).ToString();
    public static readonly string Crlf = (char)13 + Lf;

    readonly TempDir _tmp = new();
    readonly List<(string Name, string Folder)> _roots = [("Base game", "base")];
    List<ContentSource>? _sources;

    public MarketFixture() => _tmp.Mkdir("base");

    /// <summary>A raw string with its line breaks turned into LF, whatever this source file uses.</summary>
    public static string LfText(string text) => text.Replace(Crlf, Lf);

    public MarketFixture Base(string rel, string text) => Bytes("Base game", rel, Encoding.UTF8.GetBytes(text));

    public MarketFixture Mod(string name, string rel, string text) => Bytes(name, rel, Encoding.UTF8.GetBytes(text));

    /// <summary>Writes a file into the named root ("Base game" or a mod, created on first use), exactly as given.</summary>
    public MarketFixture Bytes(string name, string rel, byte[] bytes)
    {
        if (_sources is not null) throw new InvalidOperationException("Sources are already open.");
        var root = _roots.FirstOrDefault(r => r.Name == name).Folder;
        if (root is null)
        {
            root = "mod" + _roots.Count;
            _roots.Add((name, root));
            _tmp.Mkdir(root);
        }
        var full = Path.Combine(_tmp.Path, root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return this;
    }

    public IReadOnlyList<ContentSource> Sources => _sources ??= _roots
        .Select(r => ContentSource.FromPath(r.Name, Path.Combine(_tmp.Path, r.Folder), isBaseGame: r.Name == "Base game"))
        .ToList();

    public List<string> Warnings { get; } = [];

    public MarketCatalog Catalog(Localisation? loc = null) => MarketCatalog.Build(Sources, loc, Warnings);

    public string TempPath => _tmp.Path;

    public void Dispose()
    {
        foreach (var s in _sources ?? []) s.Dispose();
        _tmp.Dispose();
    }
}

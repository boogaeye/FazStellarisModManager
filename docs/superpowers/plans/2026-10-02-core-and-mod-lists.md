# Core & Mod Lists Implementation Plan (Sub-project 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A WPF + Blazor Hybrid desktop app that lists installed Stellaris mods. It lets the user build and save named mod lists, write a list to `dlc_load.json` (with a backup), and launch Stellaris. It also has a tested Core library that can snapshot and diff mod setups, which sub-project 2 (multiplayer sync) builds on.

**Architecture:**
- `FazStellarisModmanager.Core` (net10.0) holds all logic as small, focused, testable classes. Most are static and work on explicit paths.
- `ModManagerService` is the one stateful facade the UI injects.
- `FazStellarisModmanager` (net10.0-windows10.0.19041.0; WebView2 needs the Windows SDK projection) is a WPF shell hosting a `BlazorWebView` with Razor pages.
- Hashing, snapshot and diff logic is ported from HashCoop (`C:\Users\SCP Fazbear\Downloads\HashCoop\StellarisHasher`), with these changes:
  - it returns structured data instead of printing;
  - it uses a real Paradox-script parser;
  - it caches hashes;
  - it uses case-insensitive keys.

**Tech Stack:** .NET 10, C#, WPF, `Microsoft.AspNetCore.Components.WebView.Wpf`, xUnit, and `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-10-02-mod-manager-design.md`

**Conventions used by every task:**
- Repo root: `C:\Users\SCP Fazbear\source\repos\FazStellarisModmanager` (all paths are relative to it). Run commands from the root in Git Bash.
- **Mod keys:** `ugc:<id>` only for descriptors named `mod/ugc_<id>.mod`; everything else is `local:<file>.mod`. Local copies that keep a `remote_file_id` (for example Irony-merged collections like `mod/8cde_010c1b4b.mod`) stay `local:`, so they never collide with the real Workshop item.
- **Relative paths** inside snapshots and lists always use `/`.
- **Commits** end with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File structure

```
FazStellarisModmanager.sln
FazStellarisModmanager/                      (WPF + Blazor app)
  FazStellarisModmanager.csproj
  App.xaml, App.xaml.cs                      WPF application
  MainWindow.xaml, MainWindow.xaml.cs        hosts BlazorWebView, builds DI container
  AppServices.cs                             DI registrations (+ --data-dir arg)
  Routes.razor                               Blazor router (renamed from App.razor to avoid clashing with WPF App)
  MainLayout.razor                           tab navigation
  _Imports.razor
  Pages/Mods.razor                           library + list editor, Apply / Launch
  Pages/Settings.razor                       game dir / user dir / player name
  wwwroot/index.html, wwwroot/css/site.css
FazStellarisModmanager.Core/
  AppPaths.cs                                %AppData%\FazStellarisModmanager\{lists,backups,hashcache.json,settings.json}
  AppSettings.cs                             AppSettings record + SettingsStore
  ModManagerService.cs                       stateful facade for the UI
  Descriptors/ParadoxScriptParser.cs         tokenizer + PdxBlock/PdxEntry tree
  Descriptors/ModDescriptor.cs               typed .mod descriptor, Parse/Serialize
  Paths/GameLocator.cs                       user dir, Steam libraries, game dir, workshop dir
  Game/DlcLoadFile.cs                        DlcLoad record, read/write with backup
  Game/GameLauncher.cs                       start stellaris.exe, Steam running check
  Library/ModKeys.cs                         key rule (ugc:/local:)
  Library/InstalledMod.cs                    InstalledMod record, ModSource enum
  Library/ModLibrary.cs                      scan mod/*.mod, create missing ugc_<id>.mod
  Lists/ModList.cs                           ModList / ModListEntry records, dlc_load conversion
  Lists/ModListStore.cs                      JSON persistence of named lists
  Hashing/Manifest.cs                        checksum_manifest.txt parser (ported)
  Hashing/FileCollector.cs                   manifest-filtered file collection (ported)
  Hashing/HashCache.cs                       MD5 cache keyed by path+size+mtime
  Snapshots/Snapshots.cs                     ModFile / ModSnapshot / MachineSnapshot (ported)
  Snapshots/SnapshotScanner.cs               base + DLC + enabled mods snapshot (ported, async, cached)
  Diff/ModDiffer.cs                          structured diff: DiffResult / UnitDiff / FileDiff
FazStellarisModmanager.Tests/
  TestUtil/TempDir.cs, TestUtil/FakeInstall.cs
  <one test file per Core component>
```

---

### Task 1: Restructure the solution and get an empty WPF + Blazor window

**Files:**
- Delete: `FazStellarisModmanager/Program.cs`, `FazStellarisModmanager/Pages/_Host.cshtml`, `FazStellarisModmanager/Pages/Index.razor`, `FazStellarisModmanager/appsettings.json`, `FazStellarisModmanager/appsettings.Development.json`, `FazStellarisModmanager/Properties/launchSettings.json`, `FazStellarisModmanager/FazStellarisModmanager.csproj.user`, `FazStellarisModmanager/App.razor`
- Replace: `FazStellarisModmanager/FazStellarisModmanager.csproj`, `FazStellarisModmanager/MainLayout.razor`, `FazStellarisModmanager/_Imports.razor`, `FazStellarisModmanager/wwwroot/css/site.css`
- Create: `FazStellarisModmanager/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Routes.razor`, `Pages/Mods.razor` (placeholder), `wwwroot/index.html`
- Create projects: `FazStellarisModmanager.Core/`, `FazStellarisModmanager.Tests/`

- [ ] **Step 1: Delete the Blazor Server skeleton files**

```bash
cd FazStellarisModmanager
rm -f Program.cs Pages/_Host.cshtml Pages/Index.razor appsettings.json appsettings.Development.json Properties/launchSettings.json FazStellarisModmanager.csproj.user App.razor
rmdir Properties 2>/dev/null; rm -rf bin obj
cd ..
```

- [ ] **Step 2: Replace `FazStellarisModmanager/FazStellarisModmanager.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>FazStellarisModmanager</RootNamespace>
  </PropertyGroup>

</Project>
```

Then add the BlazorWebView package (this pins the latest version):

```bash
dotnet add FazStellarisModmanager package Microsoft.AspNetCore.Components.WebView.Wpf
```

- [ ] **Step 3: Create `FazStellarisModmanager/App.xaml` and `App.xaml.cs`**

```xml
<Application x:Class="FazStellarisModmanager.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
</Application>
```

```csharp
using System.Windows;

namespace FazStellarisModmanager;

public partial class App : Application
{
}
```

- [ ] **Step 4: Create `FazStellarisModmanager/MainWindow.xaml` and `MainWindow.xaml.cs`**

```xml
<Window x:Class="FazStellarisModmanager.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:blazor="clr-namespace:Microsoft.AspNetCore.Components.WebView.Wpf;assembly=Microsoft.AspNetCore.Components.WebView.Wpf"
        xmlns:local="clr-namespace:FazStellarisModmanager"
        Title="Faz Stellaris Mod Manager" Height="800" Width="1280">
    <Grid>
        <blazor:BlazorWebView HostPage="wwwroot\index.html" Services="{DynamicResource services}">
            <blazor:BlazorWebView.RootComponents>
                <blazor:RootComponent Selector="#app" ComponentType="{x:Type local:Routes}" />
            </blazor:BlazorWebView.RootComponents>
        </blazor:BlazorWebView>
    </Grid>
</Window>
```

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace FazStellarisModmanager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        Resources.Add("services", services.BuildServiceProvider());
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Create the Blazor root, layout, imports and placeholder page**

`FazStellarisModmanager/Routes.razor`:

```razor
<Router AppAssembly="@typeof(Routes).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <LayoutView Layout="@typeof(MainLayout)">
            <p role="alert">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
```

`FazStellarisModmanager/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<div class="shell">
    <nav class="tabs">
        <span class="brand">Faz Mod Manager</span>
        <NavLink href="" Match="NavLinkMatch.All">Mods</NavLink>
        <NavLink href="settings">Settings</NavLink>
    </nav>
    <main>@Body</main>
</div>
```

`FazStellarisModmanager/_Imports.razor`:

```razor
@using System.IO
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using FazStellarisModmanager
```

`FazStellarisModmanager/Pages/Mods.razor` (a placeholder; Task 15 replaces it):

```razor
@page "/"

<h1>Mods</h1>
<p>Coming soon.</p>
```

- [ ] **Step 6: Create `FazStellarisModmanager/wwwroot/index.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>Faz Stellaris Mod Manager</title>
    <base href="/" />
    <link href="css/site.css" rel="stylesheet" />
</head>
<body>
    <div id="app">Loading...</div>
    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">🗙</a>
    </div>
    <script src="_framework/blazor.webview.js"></script>
</body>
</html>
```

- [ ] **Step 7: Replace `FazStellarisModmanager/wwwroot/css/site.css`**

```css
:root {
    --bg: #11151c;
    --panel: #1a2029;
    --panel-2: #222a35;
    --text: #dbe2ea;
    --muted: #8a96a6;
    --accent: #3fa7ff;
    --danger: #e5534b;
    --ok: #46c46e;
    --warn: #d6a531;
    --border: #2d3744;
}

* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--text); font-family: "Segoe UI", sans-serif; font-size: 14px; }
h1 { font-size: 1.4rem; margin: 0 0 1rem; }
h2 { font-size: 1.05rem; margin: 0; }

.shell { display: flex; flex-direction: column; height: 100vh; }
.tabs { display: flex; gap: .25rem; align-items: center; padding: .5rem 1rem; background: var(--panel); border-bottom: 1px solid var(--border); }
.tabs .brand { font-weight: 600; margin-right: 1rem; }
.tabs a { color: var(--muted); text-decoration: none; padding: .4rem .9rem; border-radius: 6px; }
.tabs a.active { color: var(--text); background: var(--panel-2); }
main { flex: 1; overflow: auto; padding: 1rem; }

button { background: var(--panel-2); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: .4rem .8rem; cursor: pointer; }
button:hover:not(:disabled) { border-color: var(--accent); }
button:disabled { opacity: .4; cursor: default; }
button.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
button.danger { color: var(--danger); }
button.small { padding: .1rem .45rem; font-size: .85rem; }
input, select { background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: .4rem .6rem; }

.form { display: flex; flex-direction: column; gap: .8rem; max-width: 640px; }
.form label { display: flex; flex-direction: column; gap: .3rem; color: var(--muted); }
.status { color: var(--muted); }
.status.error { color: var(--danger); }

.mods-page { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; height: calc(100% - 2.5rem); }
.panel { display: flex; flex-direction: column; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; min-height: 0; }
.panel header { display: flex; gap: .5rem; align-items: center; padding: .6rem; border-bottom: 1px solid var(--border); }
.panel header .search, .panel header .list-name { flex: 1; }
.toolbar { display: flex; gap: .4rem; flex-wrap: wrap; padding: .6rem; border-bottom: 1px solid var(--border); }
.mod-list { list-style: none; margin: 0; padding: .3rem; overflow: auto; flex: 1; }
.mod-list li { display: flex; gap: .5rem; align-items: center; padding: .3rem .4rem; border-radius: 5px; }
.mod-list li:hover { background: var(--panel-2); }
.mod-list li.in-list .name { color: var(--muted); }
.mod-list li.missing .name { color: var(--danger); }
.mod-list .name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mod-list .order { width: 2.2rem; text-align: right; color: var(--muted); }
.mod-list.ordered li { cursor: grab; }
.badge { font-size: .7rem; padding: .05rem .4rem; border-radius: 4px; background: var(--panel-2); color: var(--muted); }
.badge.workshop { color: var(--accent); }
.badge.local { color: var(--warn); }
.badge.missing { color: var(--danger); }

#blazor-error-ui { background: lightyellow; color: #000; bottom: 0; box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2); display: none; left: 0; padding: 0.6rem 1.25rem 0.7rem 1.25rem; position: fixed; width: 100%; z-index: 1000; }
#blazor-error-ui .dismiss { cursor: pointer; position: absolute; right: 3.5rem; top: 0.5rem; }
```

- [ ] **Step 8: Create the Core and Tests projects and wire up the solution**

```bash
dotnet new classlib -n FazStellarisModmanager.Core -f net10.0 -o FazStellarisModmanager.Core
rm FazStellarisModmanager.Core/Class1.cs
dotnet new xunit -n FazStellarisModmanager.Tests -f net10.0 -o FazStellarisModmanager.Tests
rm FazStellarisModmanager.Tests/UnitTest1.cs
dotnet add FazStellarisModmanager reference FazStellarisModmanager.Core
dotnet add FazStellarisModmanager.Tests reference FazStellarisModmanager.Core
dotnet sln FazStellarisModmanager.sln add FazStellarisModmanager.Core FazStellarisModmanager.Tests
```

- [ ] **Step 9: Build and run the window**

Run: `dotnet build FazStellarisModmanager.sln`
Expected: `Build succeeded` with 0 errors.

Run: `dotnet run --project FazStellarisModmanager`
Expected: a window titled "Faz Stellaris Mod Manager" with a "Mods / Settings" tab bar and "Mods — Coming soon." Clicking Settings shows "Sorry, there's nothing at this address." (that page is added in Task 14). Close the window.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "Restructure into WPF Blazor Hybrid app with Core and Tests projects

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Test utilities and the Paradox script parser

**Files:**
- Create: `FazStellarisModmanager.Tests/TestUtil/TempDir.cs`
- Create: `FazStellarisModmanager.Core/Descriptors/ParadoxScriptParser.cs`
- Test: `FazStellarisModmanager.Tests/ParadoxScriptParserTests.cs`

- [ ] **Step 1: Create `FazStellarisModmanager.Tests/TestUtil/TempDir.cs`**

```csharp
namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>A unique temp directory deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsmm-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>Writes a file at a path relative to the temp dir, creating parent folders. Returns the absolute path.</summary>
    public string Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string Mkdir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 2: Write the failing tests in `FazStellarisModmanager.Tests/ParadoxScriptParserTests.cs`**

```csharp
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ParadoxScriptParserTests
{
    [Fact]
    public void Parses_key_values_blocks_and_bare_items()
    {
        var text = "\uFEFFname=\"My Mod\"\n# comment\ntags={\n\t\"Balance\"\n\t\"Gameplay\"\n}\nsupported_version=\"v4.*\"\nversion = 1.2\n";

        var b = ParadoxScriptParser.Parse(text);

        Assert.Equal("My Mod", b.GetString("name"));
        Assert.Equal("v4.*", b.GetString("supported_version"));
        Assert.Equal("1.2", b.GetString("version"));
        Assert.Equal(new[] { "Balance", "Gameplay" }, b.GetBlock("tags")!.StringItems);
    }

    [Fact]
    public void Parses_nested_blocks_and_comparison_operators()
    {
        var b = ParadoxScriptParser.Parse("tech_a = { cost = 100 weight_modifier = { factor = 2 } potential = { years_passed >= 5 } }");

        var tech = b.GetBlock("tech_a")!;
        Assert.Equal("100", tech.GetString("cost"));
        Assert.Equal("2", tech.GetBlock("weight_modifier")!.GetString("factor"));
        var cond = tech.GetBlock("potential")!.Entries.Single();
        Assert.Equal(("years_passed", ">=", "5"), (cond.Key, cond.Op, (string)cond.Value));
    }

    [Fact]
    public void Later_duplicate_key_wins_and_stray_braces_are_ignored()
    {
        var b = ParadoxScriptParser.Parse("name=\"a\"\n}\nname=\"b\"");

        Assert.Equal("b", b.GetString("name"));
    }

    [Fact]
    public void Handles_escaped_quotes_and_case_insensitive_keys()
    {
        var b = ParadoxScriptParser.Parse("Name=\"The \\\"Best\\\" Mod\"");

        Assert.Equal("The \"Best\" Mod", b.GetString("name"));
    }

    [Fact]
    public void Empty_input_gives_empty_block()
    {
        var b = ParadoxScriptParser.Parse("");

        Assert.Empty(b.Entries);
        Assert.Empty(b.Items);
        Assert.Null(b.GetString("name"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ParadoxScriptParserTests`
Expected: build FAILS with `The type or namespace name 'Descriptors' does not exist`.

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Descriptors/ParadoxScriptParser.cs`**

```csharp
using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>A "key op value" entry. Value is either a string or a nested <see cref="PdxBlock"/>.</summary>
public sealed record PdxEntry(string Key, string Op, object Value);

/// <summary>A { ... } block (or the whole file): keyed entries plus bare items such as tag lists.</summary>
public sealed class PdxBlock
{
    public List<PdxEntry> Entries { get; } = [];

    /// <summary>Bare values inside the block: strings or anonymous nested blocks.</summary>
    public List<object> Items { get; } = [];

    public IEnumerable<string> StringItems => Items.OfType<string>();

    /// <summary>Last string value for the key (later definitions win, as in Paradox scripts).</summary>
    public string? GetString(string key) =>
        Entries.LastOrDefault(e => e.Value is string && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as string;

    public PdxBlock? GetBlock(string key) =>
        Entries.LastOrDefault(e => e.Value is PdxBlock && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as PdxBlock;
}

/// <summary>Lenient parser for Paradox script (mod descriptors, .dlc files, common/*.txt).</summary>
public static class ParadoxScriptParser
{
    enum Kind { Word, Quoted, Open, Close, Op }

    readonly record struct Token(Kind Kind, string Text);

    public static PdxBlock Parse(string text)
    {
        var tokens = Tokenize(text);
        int pos = 0;
        return ParseBlock(tokens, ref pos, topLevel: true);
    }

    static PdxBlock ParseBlock(List<Token> t, ref int pos, bool topLevel)
    {
        var block = new PdxBlock();
        while (pos < t.Count)
        {
            var tok = t[pos];
            if (tok.Kind == Kind.Close)
            {
                pos++;
                if (topLevel) continue; // stray '}' at top level: ignore
                return block;
            }
            if (tok.Kind == Kind.Open)
            {
                pos++;
                block.Items.Add(ParseBlock(t, ref pos, topLevel: false));
                continue;
            }
            if (tok.Kind == Kind.Op) { pos++; continue; } // stray operator

            if (pos + 1 < t.Count && t[pos + 1].Kind == Kind.Op)
            {
                var op = t[pos + 1].Text;
                pos += 2;
                if (pos >= t.Count) break;
                var v = t[pos];
                if (v.Kind == Kind.Open)
                {
                    pos++;
                    block.Entries.Add(new PdxEntry(tok.Text, op, ParseBlock(t, ref pos, topLevel: false)));
                }
                else if (v.Kind is Kind.Word or Kind.Quoted)
                {
                    pos++;
                    block.Entries.Add(new PdxEntry(tok.Text, op, v.Text));
                }
                // otherwise malformed ("a = }"): leave the '}' for the loop to handle
                continue;
            }

            pos++;
            block.Items.Add(tok.Text);
        }
        return block;
    }

    static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = s.Length > 0 && s[0] == '\uFEFF' ? 1 : 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '{') { list.Add(new Token(Kind.Open, "{")); i++; continue; }
            if (c == '}') { list.Add(new Token(Kind.Close, "}")); i++; continue; }
            if (c is '=' or '<' or '>' or '!')
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { list.Add(new Token(Kind.Op, s.Substring(i, 2))); i += 2; }
                else { list.Add(new Token(Kind.Op, c.ToString())); i++; }
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is '"' or '\\') { sb.Append(s[i + 1]); i += 2; continue; }
                    sb.Append(s[i]);
                    i++;
                }
                i++; // closing quote
                list.Add(new Token(Kind.Quoted, sb.ToString()));
                continue;
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '<' or '>' or '!' or '"' or '#')) i++;
            list.Add(new Token(Kind.Word, s[start..i]));
        }
        return list;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ParadoxScriptParserTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add Paradox script parser

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: ModDescriptor

**Files:**
- Create: `FazStellarisModmanager.Core/Descriptors/ModDescriptor.cs`
- Test: `FazStellarisModmanager.Tests/ModDescriptorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Tests;

public class ModDescriptorTests
{
    // Real descriptor shape from Documents\Paradox Interactive\Stellaris\mod
    const string Sample = """
        path="C:/Users/SCP Fazbear/Documents/Paradox Interactive/Stellaris/mod/Fazverse_4.4_with_Flamer/8cde_010c1b4b"
        name="(Fazverse 4.4 with Flamer) Ancient Cache of Technologies: Extra Defines and Changes"
        picture="thumbnail.png"
        remote_file_id="2288335512"
        tags={
        	"Balance"
        	"Gameplay"
        }
        supported_version="v4.*"
        """;

    [Fact]
    public void Parses_real_descriptor()
    {
        var d = ModDescriptor.Parse(Sample);

        Assert.Equal("(Fazverse 4.4 with Flamer) Ancient Cache of Technologies: Extra Defines and Changes", d.Name);
        Assert.Equal("C:/Users/SCP Fazbear/Documents/Paradox Interactive/Stellaris/mod/Fazverse_4.4_with_Flamer/8cde_010c1b4b", d.Path);
        Assert.Equal("2288335512", d.RemoteFileId);
        Assert.Equal("v4.*", d.SupportedVersion);
        Assert.Equal("thumbnail.png", d.Picture);
        Assert.Equal(new[] { "Balance", "Gameplay" }, d.Tags);
        Assert.Null(d.Archive);
        Assert.Empty(d.Dependencies);
    }

    [Fact]
    public void Serialize_round_trips()
    {
        var d = ModDescriptor.Parse(Sample) with { Dependencies = ["Some \"Quoted\" Mod"] };

        var again = ModDescriptor.Parse(d.Serialize());

        Assert.Equal(d.Name, again.Name);
        Assert.Equal(d.Path, again.Path);
        Assert.Equal(d.RemoteFileId, again.RemoteFileId);
        Assert.Equal(d.SupportedVersion, again.SupportedVersion);
        Assert.Equal(d.Tags, again.Tags);
        Assert.Equal(d.Dependencies, again.Dependencies);
    }

    [Fact]
    public void Serialize_omits_null_fields()
    {
        var d = ModDescriptor.Parse("name=\"Only Name\"");

        Assert.Equal("name=\"Only Name\"\n", d.Serialize());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModDescriptorTests`
Expected: build FAILS with `The type or namespace name 'ModDescriptor' could not be found`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Descriptors/ModDescriptor.cs`**

```csharp
using System.Text;

namespace FazStellarisModmanager.Core.Descriptors;

/// <summary>A mod/*.mod (or a Workshop descriptor.mod) file.</summary>
public sealed record ModDescriptor(
    string? Name,
    string? Path,
    string? Archive,
    string? RemoteFileId,
    string? Version,
    string? SupportedVersion,
    string? Picture,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies)
{
    public static ModDescriptor Parse(string text)
    {
        var b = ParadoxScriptParser.Parse(text);
        return new ModDescriptor(
            b.GetString("name"),
            b.GetString("path"),
            b.GetString("archive"),
            b.GetString("remote_file_id"),
            b.GetString("version"),
            b.GetString("supported_version"),
            b.GetString("picture"),
            b.GetBlock("tags")?.StringItems.ToList() ?? [],
            b.GetBlock("dependencies")?.StringItems.ToList() ?? []);
    }

    public static ModDescriptor Load(string file) => Parse(File.ReadAllText(file));

    /// <summary>Writes the descriptor in the layout the Paradox launcher uses for mod/*.mod files.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();

        void Str(string key, string? value)
        {
            if (value is not null) sb.Append(key).Append("=\"").Append(Escape(value)).Append("\"\n");
        }

        void List(string key, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.Append(key).Append("={\n");
            foreach (var item in items) sb.Append("\t\"").Append(Escape(item)).Append("\"\n");
            sb.Append("}\n");
        }

        Str("version", Version);
        List("tags", Tags);
        Str("name", Name);
        Str("picture", Picture);
        Str("supported_version", SupportedVersion);
        Str("path", Path);
        Str("archive", Archive);
        Str("remote_file_id", RemoteFileId);
        List("dependencies", Dependencies);
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("\"", "\\\"");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModDescriptorTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add ModDescriptor parse/serialize

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: GameLocator

**Files:**
- Create: `FazStellarisModmanager.Core/Paths/GameLocator.cs`
- Test: `FazStellarisModmanager.Tests/GameLocatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Paths;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GameLocatorTests
{
    [Fact]
    public void Parses_library_folders_vdf()
    {
        var vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"apps" { "228980" "1" }
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameLocator.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void Finds_game_in_second_library()
    {
        using var tmp = new TempDir();
        var lib1 = tmp.Mkdir("lib1");
        var lib2 = tmp.Mkdir("lib2");
        tmp.Write("lib2/steamapps/common/Stellaris/checksum_manifest.txt", "");

        var found = GameLocator.FindGameDir(null, [lib1, lib2]);

        Assert.Equal(Path.Combine(lib2, "steamapps", "common", "Stellaris"), found);
    }

    [Fact]
    public void Explicit_dir_is_used_only_if_valid()
    {
        using var tmp = new TempDir();
        var game = tmp.Mkdir("game");

        Assert.Null(GameLocator.FindGameDir(game, []));
        tmp.Write("game/checksum_manifest.txt", "");
        Assert.Equal(game, GameLocator.FindGameDir(game, []));
    }

    [Fact]
    public void Workshop_dir_is_in_same_library_as_game()
    {
        var dir = GameLocator.WorkshopDirFor(@"D:\SteamLibrary\steamapps\common\Stellaris");

        Assert.Equal(@"D:\SteamLibrary\steamapps\workshop\content\281990", dir);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter GameLocatorTests`
Expected: build FAILS with `The type or namespace name 'Paths' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Paths/GameLocator.cs`**

```csharp
using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Paths;

public static class GameLocator
{
    public const string StellarisAppId = "281990";

    /// <summary>Documents\Paradox Interactive\Stellaris (holds dlc_load.json and mod/*.mod).</summary>
    public static string DefaultUserDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Paradox Interactive", "Stellaris");

    /// <summary>Library roots listed in steamapps/libraryfolders.vdf.</summary>
    public static List<string> ParseLibraryFolders(string vdfText) =>
        Regex.Matches(vdfText, "\"path\"\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"))
            .ToList();

    public static string? SteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    }

    /// <summary>All Steam library roots: the Steam install plus everything in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> SteamLibraries()
    {
        var steam = SteamPath();
        if (steam is null) return [];
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        return libs.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsGameDir(string dir) => File.Exists(Path.Combine(dir, "checksum_manifest.txt"));

    /// <summary>The explicit dir if it is a valid install, otherwise the first Steam library containing Stellaris.</summary>
    public static string? FindGameDir(string? explicitDir, IEnumerable<string>? libraries = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return IsGameDir(explicitDir) ? explicitDir : null;
        foreach (var lib in libraries ?? SteamLibraries())
        {
            var candidate = Path.Combine(lib, "steamapps", "common", "Stellaris");
            if (IsGameDir(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>gameDir is &lt;lib&gt;\steamapps\common\Stellaris, so Workshop items live in &lt;lib&gt;\steamapps\workshop\content\281990.</summary>
    public static string WorkshopDirFor(string gameDir) =>
        Path.GetFullPath(Path.Combine(gameDir, "..", "..", "workshop", "content", StellarisAppId));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter GameLocatorTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add GameLocator (Steam libraries, game and workshop dirs)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: DlcLoadFile

**Files:**
- Create: `FazStellarisModmanager.Core/Game/DlcLoadFile.cs`
- Test: `FazStellarisModmanager.Tests/DlcLoadFileTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class DlcLoadFileTests
{
    [Fact]
    public void Missing_file_reads_as_empty()
    {
        using var tmp = new TempDir();

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Empty(load.EnabledMods);
        Assert.Empty(load.DisabledDlcs);
    }

    [Fact]
    public void Reads_real_format()
    {
        using var tmp = new TempDir();
        tmp.Write("dlc_load.json", "{\"disabled_dlcs\":[\"dlc/dlc001_x/dlc001.dlc\"],\"enabled_mods\":[\"mod/ugc_1.mod\",\"mod/EthicsFix.mod\"]}");

        var load = DlcLoadFile.Read(tmp.Path);

        Assert.Equal(new[] { "mod/ugc_1.mod", "mod/EthicsFix.mod" }, load.EnabledMods);
        Assert.Equal(new[] { "dlc/dlc001_x/dlc001.dlc" }, load.DisabledDlcs);
    }

    [Fact]
    public void Write_backs_up_previous_file_and_round_trips()
    {
        using var tmp = new TempDir();
        var original = "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/old.mod\"]}";
        tmp.Write("user/dlc_load.json", original);
        var userDir = Path.Combine(tmp.Path, "user");
        var backups = Path.Combine(tmp.Path, "backups");

        var backup = DlcLoadFile.Write(userDir, new DlcLoad(["mod/ugc_2.mod", "mod/a&b.mod"], []), backups);

        Assert.NotNull(backup);
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(new[] { "mod/ugc_2.mod", "mod/a&b.mod" }, DlcLoadFile.Read(userDir).EnabledMods);
        Assert.Contains("\"mod/a&b.mod\"", File.ReadAllText(Path.Combine(userDir, "dlc_load.json")));
    }

    [Fact]
    public void Write_without_existing_file_returns_null_backup()
    {
        using var tmp = new TempDir();

        var backup = DlcLoadFile.Write(tmp.Path, new DlcLoad(["mod/x.mod"], []), Path.Combine(tmp.Path, "b"));

        Assert.Null(backup);
        Assert.Single(DlcLoadFile.Read(tmp.Path).EnabledMods);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter DlcLoadFileTests`
Expected: build FAILS with `The type or namespace name 'Game' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Game/DlcLoadFile.cs`**

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Game;

/// <summary>Contents of Documents\Paradox Interactive\Stellaris\dlc_load.json. Mod paths are relative to the user dir, e.g. "mod/ugc_123.mod".</summary>
public sealed record DlcLoad(List<string> EnabledMods, List<string> DisabledDlcs);

public static class DlcLoadFile
{
    public const string FileName = "dlc_load.json";

    static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static DlcLoad Read(string userDir)
    {
        var path = Path.Combine(userDir, FileName);
        if (!File.Exists(path)) return new DlcLoad([], []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new DlcLoad(ReadArray(doc.RootElement, "enabled_mods"), ReadArray(doc.RootElement, "disabled_dlcs"));
    }

    static List<string> ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : [];

    /// <summary>Writes dlc_load.json after copying the existing file into backupDir. Returns the backup path, or null if there was no file.</summary>
    public static string? Write(string userDir, DlcLoad load, string backupDir)
    {
        var path = Path.Combine(userDir, FileName);
        string? backup = null;
        if (File.Exists(path))
        {
            Directory.CreateDirectory(backupDir);
            backup = Path.Combine(backupDir, $"dlc_load_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
            File.Copy(path, backup, overwrite: true);
        }
        Directory.CreateDirectory(userDir);
        var json = JsonSerializer.Serialize(new { disabled_dlcs = load.DisabledDlcs, enabled_mods = load.EnabledMods }, WriteOptions);
        File.WriteAllText(path, json);
        return backup;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter DlcLoadFileTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add dlc_load.json read/write with backup

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: FakeInstall fixture, ModKeys and ModLibrary

**Files:**
- Create: `FazStellarisModmanager.Tests/TestUtil/FakeInstall.cs`
- Create: `FazStellarisModmanager.Core/Library/ModKeys.cs`, `Library/InstalledMod.cs`, `Library/ModLibrary.cs`
- Test: `FazStellarisModmanager.Tests/ModLibraryTests.cs`

- [ ] **Step 1: Create `FazStellarisModmanager.Tests/TestUtil/FakeInstall.cs`**

This fixture lays out a fake Steam library containing the game and Workshop content, plus a fake Documents user dir. Later tasks reuse it.

```csharp
using System.IO.Compression;

namespace FazStellarisModmanager.Tests.TestUtil;

/// <summary>
/// lib/steamapps/common/Stellaris          game: manifest, common/*.txt, one DLC zip
/// lib/steamapps/workshop/content/281990   workshop item 111 (no mod/ugc_111.mod yet)
/// user/                                   logs/game.log, mod/local.mod (+content), dlc_load.json
/// </summary>
public sealed class FakeInstall : IDisposable
{
    readonly TempDir _tmp = new();

    public string Root => _tmp.Path;
    public string Library => Path.Combine(Root, "lib");
    public string GameDir => Path.Combine(Library, "steamapps", "common", "Stellaris");
    public string WorkshopDir => Path.Combine(Library, "steamapps", "workshop", "content", "281990");
    public string UserDir => Path.Combine(Root, "user");
    public string DataDir => Path.Combine(Root, "appdata");

    public FakeInstall()
    {
        _tmp.Write("lib/steamapps/common/Stellaris/checksum_manifest.txt",
            "directory = {\n\tname = \"common\"\n\tsub_directories = yes\n\tfile_extension = \".txt\"\n}\n");
        _tmp.Write("lib/steamapps/common/Stellaris/common/a.txt", "base a");
        _tmp.Write("lib/steamapps/common/Stellaris/common/sub/b.txt", "base b");
        _tmp.Write("lib/steamapps/common/Stellaris/common/icon.dds", "not hashed: wrong extension");
        _tmp.Write("lib/steamapps/common/Stellaris/dlc/dlc001_test/dlc001.dlc",
            "name=\"Test DLC\"\narchive=\"dlc/dlc001_test/dlc001.zip\"\nsteam_id=\"999\"\n");
        using (var zip = ZipFile.Open(Path.Combine(GameDir, "dlc", "dlc001_test", "dlc001.zip"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("music/song.ogg").Open()))
            w.Write("la la la");

        _tmp.Write("lib/steamapps/workshop/content/281990/111/descriptor.mod",
            "name=\"Workshop One\"\nsupported_version=\"v4.*\"\ntags={\n\t\"Gameplay\"\n}\n");
        _tmp.Write("lib/steamapps/workshop/content/281990/111/common/x.txt", "ws one");

        _tmp.Write("user/logs/game.log", "[00:00:00][game.cpp:100]: Game Version: Pegasus v4.4.6\r\n");
        _tmp.Write("user/mod/local.mod", "name=\"Local Mod\"\npath=\"mod/local\"\n");
        _tmp.Write("user/mod/local/common/y.txt", "local y");
        _tmp.Write("user/dlc_load.json", "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/local.mod\"]}");
    }

    public string Write(string relative, string content) => _tmp.Write(relative, content);

    public void Dispose() => _tmp.Dispose();
}
```

- [ ] **Step 2: Write the failing tests in `FazStellarisModmanager.Tests/ModLibraryTests.cs`**

```csharp
using FazStellarisModmanager.Core.Descriptors;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModLibraryTests
{
    [Theory]
    [InlineData("mod/ugc_3005307317.mod", "ugc:3005307317")]
    [InlineData("mod/UGC_42.mod", "ugc:42")]
    [InlineData("mod/EthicsFix.mod", "local:EthicsFix.mod")]
    [InlineData("mod/8cde_010c1b4b.mod", "local:8cde_010c1b4b.mod")]
    public void Keys_follow_descriptor_file_name(string rel, string expected) =>
        Assert.Equal(expected, ModKeys.For(rel));

    [Fact]
    public void WorkshopId_parses_only_ugc_keys()
    {
        Assert.Equal(42UL, ModKeys.WorkshopId("ugc:42"));
        Assert.Null(ModKeys.WorkshopId("local:x.mod"));
    }

    [Fact]
    public void Creates_missing_workshop_descriptor_once()
    {
        using var fake = new FakeInstall();

        var created = ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        var again = ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);

        Assert.Equal(new[] { "mod/ugc_111.mod" }, created);
        Assert.Empty(again);
        var d = ModDescriptor.Load(Path.Combine(fake.UserDir, "mod", "ugc_111.mod"));
        Assert.Equal("Workshop One", d.Name);
        Assert.Equal("111", d.RemoteFileId);
        Assert.Equal(Path.Combine(fake.WorkshopDir, "111").Replace('\\', '/'), d.Path);
        Assert.Equal(new[] { "Gameplay" }, d.Tags);
    }

    [Fact]
    public void Missing_workshop_dir_creates_nothing()
    {
        using var fake = new FakeInstall();

        Assert.Empty(ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, Path.Combine(fake.Root, "nope")));
    }

    [Fact]
    public void Scan_lists_workshop_and_local_mods()
    {
        using var fake = new FakeInstall();
        ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        // Irony-style local copy that keeps the remote id: must stay local
        fake.Write("user/mod/8cde_copy.mod", "name=\"Copy\"\npath=\"mod/copy\"\nremote_file_id=\"111\"\n");

        var mods = ModLibrary.Scan(fake.UserDir);

        Assert.Equal(new[] { "local:8cde_copy.mod", "local:local.mod", "ugc:111" }, mods.Select(m => m.Key));
        var ws = mods.Single(m => m.Key == "ugc:111");
        Assert.Equal(ModSource.Workshop, ws.Source);
        Assert.Equal("mod/ugc_111.mod", ws.DescriptorRel);
        Assert.Equal(Path.Combine(fake.WorkshopDir, "111"), ws.ContentPath);
        var local = mods.Single(m => m.Key == "local:local.mod");
        Assert.Equal(ModSource.Local, local.Source);
        Assert.Equal(Path.Combine(fake.UserDir, "mod", "local"), local.ContentPath);
        Assert.Equal("111", mods.Single(m => m.Key == "local:8cde_copy.mod").RemoteId);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModLibraryTests`
Expected: build FAILS with `The type or namespace name 'Library' does not exist`.

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Library/ModKeys.cs`**

```csharp
using System.Text.RegularExpressions;

namespace FazStellarisModmanager.Core.Library;

public static class ModKeys
{
    static readonly Regex Ugc = new(@"^ugc_(\d+)\.mod$", RegexOptions.IgnoreCase);

    /// <summary>
    /// "ugc:&lt;id&gt;" for Steam Workshop descriptors (mod/ugc_&lt;id&gt;.mod), otherwise "local:&lt;file&gt;".
    /// Local copies that keep a remote_file_id (e.g. Irony merged collections) stay local,
    /// so they never collide with the real Workshop item.
    /// </summary>
    public static string For(string descriptorRel)
    {
        var file = Path.GetFileName(descriptorRel.Replace('\\', '/'));
        var m = Ugc.Match(file);
        return m.Success ? $"ugc:{m.Groups[1].Value}" : $"local:{file}";
    }

    public static ulong? WorkshopId(string key) =>
        key.StartsWith("ugc:", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(key[4..], out var id) ? id : null;
}
```

- [ ] **Step 5: Implement `FazStellarisModmanager.Core/Library/InstalledMod.cs`**

```csharp
namespace FazStellarisModmanager.Core.Library;

public enum ModSource { Workshop, Local }

/// <summary>A mod with a descriptor in Documents\...\Stellaris\mod. DescriptorRel is e.g. "mod/ugc_123.mod".</summary>
public sealed record InstalledMod(
    string Key,
    string Name,
    string DescriptorRel,
    string? RemoteId,
    string? Version,
    string? SupportedVersion,
    string ContentPath,
    ModSource Source,
    IReadOnlyList<string> Tags);
```

- [ ] **Step 6: Implement `FazStellarisModmanager.Core/Library/ModLibrary.cs`**

```csharp
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Library;

public static class ModLibrary
{
    /// <summary>
    /// Creates mod/ugc_&lt;id&gt;.mod for every Workshop folder that lacks one, the way the Paradox launcher does.
    /// Returns the descriptors created (relative to the user dir).
    /// </summary>
    public static List<string> EnsureWorkshopDescriptors(string userDir, string workshopDir)
    {
        var created = new List<string>();
        if (!Directory.Exists(workshopDir)) return created;
        var modDir = Path.Combine(userDir, "mod");
        Directory.CreateDirectory(modDir);

        foreach (var dir in Directory.EnumerateDirectories(workshopDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(dir);
            if (!ulong.TryParse(id, out _)) continue;
            var target = Path.Combine(modDir, $"ugc_{id}.mod");
            if (File.Exists(target)) continue;
            var inner = Path.Combine(dir, "descriptor.mod");
            if (!File.Exists(inner)) continue;

            var full = Path.GetFullPath(dir).Replace('\\', '/');
            var d = ModDescriptor.Load(inner);
            d = d.Archive is { Length: > 0 } archive
                ? d with { Path = null, Archive = $"{full}/{Path.GetFileName(archive)}", RemoteFileId = id }
                : d with { Path = full, Archive = null, RemoteFileId = id };
            File.WriteAllText(target, d.Serialize());
            created.Add($"mod/ugc_{id}.mod");
        }
        return created;
    }

    /// <summary>Reads every mod/*.mod descriptor in the user dir, sorted by file name.</summary>
    public static List<InstalledMod> Scan(string userDir)
    {
        var result = new List<InstalledMod>();
        var modDir = Path.Combine(userDir, "mod");
        if (!Directory.Exists(modDir)) return result;

        foreach (var file in Directory.EnumerateFiles(modDir, "*.mod").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var rel = "mod/" + Path.GetFileName(file);
            var d = ModDescriptor.Load(file);
            var key = ModKeys.For(rel);
            result.Add(new InstalledMod(
                key,
                d.Name ?? Path.GetFileNameWithoutExtension(file),
                rel,
                d.RemoteFileId,
                d.Version,
                d.SupportedVersion,
                ResolveContent(userDir, d),
                key.StartsWith("ugc:", StringComparison.Ordinal) ? ModSource.Workshop : ModSource.Local,
                d.Tags));
        }
        return result;
    }

    /// <summary>Absolute content folder (path=) or archive (archive=); relative values are relative to the user dir.</summary>
    public static string ResolveContent(string userDir, ModDescriptor d)
    {
        var p = d.Path ?? d.Archive ?? "";
        if (p.Length == 0) return "";
        return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(userDir, p));
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModLibraryTests`
Expected: `Passed!  - Failed: 0, Passed: 8`

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Add mod library scan and workshop descriptor creation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: ModList and ModListStore

**Files:**
- Create: `FazStellarisModmanager.Core/Lists/ModList.cs`, `Lists/ModListStore.cs`
- Test: `FazStellarisModmanager.Tests/ModListTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModListTests
{
    static InstalledMod Installed(string rel, string name) =>
        new(ModKeys.For(rel), name, rel, null, null, null, "", ModSource.Local, []);

    [Fact]
    public void FromDlcLoad_uses_library_names_and_keeps_unknown_entries()
    {
        var load = new DlcLoad(["mod/ugc_5.mod", "mod/gone.mod"], ["dlc/x.dlc"]);
        var library = new[] { Installed("mod/ugc_5.mod", "Five") };

        var list = ModList.FromDlcLoad("Current", load, library);

        Assert.Equal("Current", list.Name);
        Assert.Equal(new[] { ("ugc:5", "Five"), ("local:gone.mod", "gone") }, list.Mods.Select(m => (m.Key, m.Name)));
        Assert.Equal(new[] { "dlc/x.dlc" }, list.DisabledDlcs);
    }

    [Fact]
    public void ToDlcLoad_preserves_order()
    {
        var list = new ModList("L", [new("ugc:2", "B", "mod/ugc_2.mod", "2"), new("ugc:1", "A", "mod/ugc_1.mod", "1")], []);

        Assert.Equal(new[] { "mod/ugc_2.mod", "mod/ugc_1.mod" }, list.ToDlcLoad().EnabledMods);
    }

    [Fact]
    public void Store_saves_loads_lists_and_deletes()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(Path.Combine(tmp.Path, "lists"));
        var b = new ModList("b: weird/name?", [new("ugc:1", "A", "mod/ugc_1.mod", "1")], []);
        var a = new ModList("a list", [], ["dlc/x.dlc"]);

        store.Save(b);
        store.Save(a);

        Assert.Equal(new[] { "a list", "b: weird/name?" }, store.LoadAll().Select(l => l.Name));
        var loaded = store.Load("b: weird/name?")!;
        Assert.Equal("mod/ugc_1.mod", loaded.Mods.Single().DescriptorRel);
        store.Delete("a list");
        Assert.Null(store.Load("a list"));
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void Store_rejects_blank_name()
    {
        using var tmp = new TempDir();
        var store = new ModListStore(tmp.Path);

        Assert.Throws<ArgumentException>(() => store.Save(new ModList("  ", [], [])));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModListTests`
Expected: build FAILS with `The type or namespace name 'Lists' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Lists/ModList.cs`**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;

namespace FazStellarisModmanager.Core.Lists;

public sealed record ModListEntry(string Key, string Name, string DescriptorRel, string? RemoteId);

/// <summary>A named, ordered mod list owned by the app. Mods are in load order.</summary>
public sealed record ModList(string Name, List<ModListEntry> Mods, List<string> DisabledDlcs)
{
    public DlcLoad ToDlcLoad() => new(Mods.Select(m => m.DescriptorRel).ToList(), DisabledDlcs.ToList());

    public static ModList FromDlcLoad(string name, DlcLoad load, IReadOnlyList<InstalledMod> library)
    {
        var byRel = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in library) byRel.TryAdd(m.DescriptorRel, m);

        var mods = load.EnabledMods.Select(raw =>
        {
            var rel = raw.Replace('\\', '/');
            return byRel.TryGetValue(rel, out var m)
                ? new ModListEntry(m.Key, m.Name, m.DescriptorRel, m.RemoteId)
                : new ModListEntry(ModKeys.For(rel), Path.GetFileNameWithoutExtension(rel), rel, null);
        }).ToList();

        return new ModList(name, mods, load.DisabledDlcs.ToList());
    }
}
```

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Lists/ModListStore.cs`**

```csharp
using System.Text.Json;

namespace FazStellarisModmanager.Core.Lists;

/// <summary>Stores each list as &lt;directory&gt;\&lt;sanitized name&gt;.json.</summary>
public sealed class ModListStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Directory { get; } = directory;

    public IReadOnlyList<ModList> LoadAll() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(ReadFile)
                .OfType<ModList>()
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public ModList? Load(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? ReadFile(path) : null;
    }

    public void Save(ModList list)
    {
        var path = PathFor(list.Name);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, JsonSerializer.Serialize(list, Json));
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    string PathFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length == 0) throw new ArgumentException("List name is empty.", nameof(name));
        return Path.Combine(Directory, safe + ".json");
    }

    static ModList? ReadFile(string path)
    {
        try { return JsonSerializer.Deserialize<ModList>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; } // a corrupt list file is skipped, not fatal
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModListTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add mod lists and JSON list store

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: AppPaths and settings

**Files:**
- Create: `FazStellarisModmanager.Core/AppPaths.cs`, `FazStellarisModmanager.Core/AppSettings.cs`
- Test: `FazStellarisModmanager.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Paths_live_under_root()
    {
        var p = new AppPaths(@"C:\data");

        Assert.Equal(@"C:\data\lists", p.Lists);
        Assert.Equal(@"C:\data\backups", p.Backups);
        Assert.Equal(@"C:\data\hashcache.json", p.HashCache);
        Assert.Equal(@"C:\data\settings.json", p.Settings);
    }

    [Fact]
    public void Missing_settings_file_gives_defaults()
    {
        using var tmp = new TempDir();

        Assert.Equal(new AppSettings(), SettingsStore.Load(Path.Combine(tmp.Path, "settings.json")));
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "sub", "settings.json");
        var s = new AppSettings(GameDir: @"D:\Games\Stellaris", UserDir: null, PlayerName: "Faz");

        SettingsStore.Save(file, s);

        Assert.Equal(s, SettingsStore.Load(file));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter AppSettingsTests`
Expected: build FAILS with `The type or namespace name 'AppPaths' could not be found`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/AppPaths.cs`**

```csharp
namespace FazStellarisModmanager.Core;

/// <summary>Where the app keeps its own data. Default: %AppData%\FazStellarisModmanager.</summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FazStellarisModmanager"));

    public string Root { get; } = root;
    public string Lists => Path.Combine(Root, "lists");
    public string Backups => Path.Combine(Root, "backups");
    public string HashCache => Path.Combine(Root, "hashcache.json");
    public string Settings => Path.Combine(Root, "settings.json");
}
```

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/AppSettings.cs`**

```csharp
using System.Text.Json;

namespace FazStellarisModmanager.Core;

/// <summary>User overrides. Null means auto-detect (game dir, user dir) or machine name (player name).</summary>
public sealed record AppSettings(string? GameDir = null, string? UserDir = null, string? PlayerName = null);

public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string file)
    {
        if (!File.Exists(file)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? new AppSettings();
    }

    public static void Save(string file, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(settings, Json));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter AppSettingsTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add app paths and settings store

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Hashing: manifest, file collector, hash cache

**Files:**
- Create: `FazStellarisModmanager.Core/Hashing/Manifest.cs`, `Hashing/FileCollector.cs`, `Hashing/HashCache.cs`
- Test: `FazStellarisModmanager.Tests/HashingTests.cs`

Ported from HashCoop `Program.cs` (`Manifest.Parse`, `Hasher.CollectFiles`), with two changes:
- Keys are case-insensitive.
- `Hasher.Compute` (the old single 4-character checksum guess) is dropped, because nothing in the app uses it.

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class HashingTests
{
    const string ManifestText = """
        # Stellaris checksum manifest
        directory = {
        	name = "common"
        	sub_directories = yes
        	file_extension = ".txt"
        }
        directory = {
        	name = "events"
        	sub_directories = no
        	file_extension = txt
        }
        """;

    [Fact]
    public void Parses_manifest()
    {
        var rules = Manifest.Parse(ManifestText);

        Assert.Equal(new[]
        {
            new ManifestEntry("common", true, ".txt"),
            new ManifestEntry("events", false, ".txt"),
        }, rules);
    }

    [Fact]
    public void Collects_matching_files_and_later_roots_override()
    {
        using var tmp = new TempDir();
        tmp.Write("game/common/a.txt", "1");
        tmp.Write("game/common/deep/b.txt", "2");
        tmp.Write("game/common/c.dds", "skip");
        tmp.Write("game/events/e.txt", "3");
        tmp.Write("game/events/deep/skip.txt", "not recursive");
        tmp.Write("mod/common/A.txt", "override, different case");

        var files = FileCollector.Collect(Manifest.Parse(ManifestText),
            [Path.Combine(tmp.Path, "game"), Path.Combine(tmp.Path, "mod")]);

        Assert.Equal(new[] { "common/a.txt", "common/deep/b.txt", "events/e.txt" }, files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(tmp.Path, "mod", "common", "A.txt"), files["common/a.txt"]);
    }

    [Fact]
    public void HashCache_computes_md5_and_reuses_it_until_file_changes()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("f.txt", "abc");
        var cache = HashCache.InMemory();

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", cache.GetMd5(file, out var size));
        Assert.Equal(3, size);
        cache.GetMd5(file, out _);
        Assert.Equal(1, cache.Misses);

        File.WriteAllText(file, "abcd");
        Assert.Equal("e2fc714c4727ee9395f324cd2e7f331f", cache.GetMd5(file, out _));
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void HashCache_persists()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("f.txt", "abc");
        var cachePath = Path.Combine(tmp.Path, "data", "hashcache.json");
        var cache = HashCache.Load(cachePath);
        cache.GetMd5(file, out _);
        cache.Save();

        var reloaded = HashCache.Load(cachePath);
        reloaded.GetMd5(file, out _);

        Assert.Equal(0, reloaded.Misses);
    }

    [Fact]
    public void Corrupt_cache_file_starts_empty()
    {
        using var tmp = new TempDir();
        var cachePath = tmp.Write("hashcache.json", "{not json");

        Assert.Equal(0, HashCache.Load(cachePath).Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter HashingTests`
Expected: build FAILS with `The type or namespace name 'Hashing' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Hashing/Manifest.cs`**

```csharp
namespace FazStellarisModmanager.Core.Hashing;

/// <summary>One "directory { ... }" block from checksum_manifest.txt.</summary>
public sealed record ManifestEntry(string Name, bool SubDirectories, string FileExtension);

/// <summary>Line-based parser for &lt;game&gt;\checksum_manifest.txt (ported from HashCoop).</summary>
public static class Manifest
{
    public static List<ManifestEntry> Parse(string text)
    {
        var entries = new List<ManifestEntry>();
        string? name = null;
        bool sub = false;
        string? ext = null;

        void Flush()
        {
            if (name is not null && ext is not null) entries.Add(new ManifestEntry(name, sub, ext));
            name = null;
            sub = false;
            ext = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("directory", StringComparison.OrdinalIgnoreCase)) { Flush(); continue; }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"');
            switch (key)
            {
                case "name": name = val.Replace('\\', '/').TrimEnd('/'); break;
                case "sub_directories": sub = val.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                case "file_extension": ext = val.StartsWith('.') ? val : "." + val; break;
            }
        }
        Flush();
        return entries;
    }
}
```

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Hashing/FileCollector.cs`**

```csharp
namespace FazStellarisModmanager.Core.Hashing;

public static class FileCollector
{
    /// <summary>
    /// Relative path ("common/a.txt") -> absolute path for files matched by the manifest rules.
    /// Later roots override earlier ones, matching how Stellaris layers mods over the base game.
    /// </summary>
    public static SortedDictionary<string, string> Collect(IEnumerable<ManifestEntry> entries, IReadOnlyList<string> roots)
    {
        var rules = entries.ToList();
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var entry in rules)
            {
                var dir = Path.Combine(root, entry.Name);
                if (!Directory.Exists(dir)) continue;
                var opt = entry.SubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
                {
                    if (!f.EndsWith(entry.FileExtension, StringComparison.OrdinalIgnoreCase)) continue;
                    files[Path.GetRelativePath(root, f).Replace('\\', '/')] = f;
                }
            }
        }
        return files;
    }
}
```

- [ ] **Step 5: Implement `FazStellarisModmanager.Core/Hashing/HashCache.cs`**

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Hashing;

internal sealed record HashCacheEntry(long Size, long MtimeTicks, string Md5);

/// <summary>Thread-safe MD5 cache keyed by full path; an entry is reused while size and mtime are unchanged.</summary>
public sealed class HashCache
{
    readonly ConcurrentDictionary<string, HashCacheEntry> _entries;
    readonly string? _file;
    int _misses;

    HashCache(string? file, ConcurrentDictionary<string, HashCacheEntry> entries)
    {
        _file = file;
        _entries = entries;
    }

    public static HashCache InMemory() => new(null, new(StringComparer.OrdinalIgnoreCase));

    public static HashCache Load(string file)
    {
        var entries = new ConcurrentDictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(file))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, HashCacheEntry>>(File.ReadAllText(file));
                if (data is not null)
                    foreach (var (k, v) in data) entries[k] = v;
            }
            catch (JsonException)
            {
                // Corrupt cache: start empty. It is only a cache, so this is safe.
            }
        }
        return new HashCache(file, entries);
    }

    public int Count => _entries.Count;

    /// <summary>Number of files actually hashed (cache misses) since this instance was created.</summary>
    public int Misses => _misses;

    /// <summary>Lower-case hex MD5 of the file.</summary>
    public string GetMd5(string absPath, out long size)
    {
        var info = new FileInfo(absPath);
        size = info.Length;
        var key = Path.GetFullPath(absPath);
        var mtime = info.LastWriteTimeUtc.Ticks;
        if (_entries.TryGetValue(key, out var e) && e.Size == size && e.MtimeTicks == mtime) return e.Md5;

        Interlocked.Increment(ref _misses);
        string md5;
        using (var fs = File.OpenRead(absPath)) md5 = Convert.ToHexStringLower(MD5.HashData(fs));
        _entries[key] = new HashCacheEntry(size, mtime, md5);
        return md5;
    }

    public void Save()
    {
        if (_file is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Dictionary<string, HashCacheEntry>(_entries)));
        File.Move(tmp, _file, overwrite: true);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter HashingTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Port manifest parsing and file collection; add hash cache

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Snapshot records and SnapshotScanner

**Files:**
- Create: `FazStellarisModmanager.Core/Snapshots/Snapshots.cs`, `Snapshots/SnapshotScanner.cs`
- Test: `FazStellarisModmanager.Tests/SnapshotScannerTests.cs`

Ported from HashCoop `Mods.cs` `ModScanner.Scan`, with these changes:
- It is async, hashes files in parallel and uses the cache.
- It uses `ModKeys` and `ModDescriptor`.
- It skips duplicate enabled mods.
- DLCs are hashed from the zip's central-directory CRCs only (the whole-archive MD5 is dropped because it's slow and redundant).

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Hashing;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Snapshots;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class SnapshotScannerTests
{
    [Fact]
    public async Task Scans_base_dlc_and_enabled_mods_in_load_order()
    {
        using var fake = new FakeInstall();
        ModLibrary.EnsureWorkshopDescriptors(fake.UserDir, fake.WorkshopDir);
        fake.Write("user/dlc_load.json",
            "{\"disabled_dlcs\":[],\"enabled_mods\":[\"mod/ugc_111.mod\",\"mod/local.mod\",\"mod/ugc_111.mod\",\"mod/gone.mod\"]}");

        var snap = await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", HashCache.InMemory());

        Assert.Equal("PC1", snap.Machine);
        Assert.Equal("Pegasus v4.4.6", snap.GameVersion);
        Assert.Equal(new[] { "common/a.txt", "common/sub/b.txt" }, snap.Base.Files.Select(f => f.Path));

        var dlc = Assert.Single(snap.Dlcs);
        Assert.Equal("dlc:dlc001", dlc.Key);
        Assert.Equal("Test DLC", dlc.Name);
        Assert.StartsWith("crc32:", Assert.Single(dlc.Files).Md5);

        Assert.Equal(new[] { ("ugc:111", 1), ("local:local.mod", 2), ("local:gone.mod", 3) },
            snap.Mods.Select(m => (m.Key, m.LoadOrder)));
        Assert.Equal(new[] { "common/x.txt", "descriptor.mod" }, snap.Mods[0].Files.Select(f => f.Path));
        Assert.Equal(new[] { "common/y.txt" }, snap.Mods[1].Files.Select(f => f.Path));
        Assert.Empty(snap.Mods[2].Files);
    }

    [Fact]
    public async Task Disabled_dlc_is_skipped()
    {
        using var fake = new FakeInstall();
        fake.Write("user/dlc_load.json", "{\"disabled_dlcs\":[\"dlc/dlc001_test/dlc001.dlc\"],\"enabled_mods\":[]}");

        var snap = await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", HashCache.InMemory());

        Assert.Empty(snap.Dlcs);
    }

    [Fact]
    public async Task Second_scan_hits_cache()
    {
        using var fake = new FakeInstall();
        var cache = HashCache.InMemory();

        await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", cache);
        var missesAfterFirst = cache.Misses;
        await SnapshotScanner.ScanAsync(fake.UserDir, fake.GameDir, "PC1", cache);

        Assert.True(missesAfterFirst > 0);
        Assert.Equal(missesAfterFirst, cache.Misses);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter SnapshotScannerTests`
Expected: build FAILS with `The type or namespace name 'Snapshots' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Snapshots/Snapshots.cs`**

```csharp
namespace FazStellarisModmanager.Core.Snapshots;

/// <summary>A file inside a content unit: relative path and MD5 (or "crc32:xxxxxxxx" for DLC zip entries).</summary>
public sealed record ModFile(string Path, string Md5, long Size);

/// <summary>One unit of content (base game, a DLC, or a mod) as seen on a machine.</summary>
public sealed record ModSnapshot(
    string Key,              // "base", "dlc:dlc001", "ugc:123", "local:foo.mod"
    string Name,
    string Descriptor,       // e.g. mod/ugc_123.mod or dlc/dlc001_x/dlc001.dlc
    string? RemoteId,
    string? Version,
    string? SupportedVersion,
    string ContentPath,
    int LoadOrder,
    List<ModFile> Files);

/// <summary>Everything one machine reports about its game setup.</summary>
public sealed record MachineSnapshot(
    string Machine,
    string GameVersion,
    string GameDir,
    DateTime TakenUtc,
    ModSnapshot Base,
    List<ModSnapshot> Dlcs,
    List<ModSnapshot> Mods);
```

- [ ] **Step 4: Implement `FazStellarisModmanager.Core/Snapshots/SnapshotScanner.cs`**

```csharp
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
    /// <summary>Snapshots the base game, enabled DLCs and the mods enabled in dlc_load.json (in load order).</summary>
    public static async Task<MachineSnapshot> ScanAsync(string userDir, string gameDir, string machineName, HashCache cache,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var load = DlcLoadFile.Read(userDir);

        var manifestPath = Path.Combine(gameDir, "checksum_manifest.txt");
        var rules = File.Exists(manifestPath) ? Manifest.Parse(await File.ReadAllTextAsync(manifestPath, ct)) : [];
        progress?.Report($"Hashing base game: {gameDir}");
        var baseFiles = await HashAllAsync(FileCollector.Collect(rules, [gameDir]).Select(kv => (kv.Key, kv.Value)), cache, ct);
        var gameVersion = ReadGameVersion(userDir, progress);
        var baseSnap = new ModSnapshot("base", "Stellaris", "checksum_manifest.txt", null, gameVersion, null, gameDir, 0, baseFiles);

        var dlcs = ScanDlcs(gameDir, load, progress);

        var mods = new List<ModSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int order = 0;
        foreach (var raw in load.EnabledMods)
        {
            ct.ThrowIfCancellationRequested();
            var rel = raw.Replace('\\', '/');
            var key = ModKeys.For(rel);
            if (!seen.Add(key)) { progress?.Report($"  [duplicate] {rel} skipped"); continue; }
            order++;

            var descriptorPath = Path.Combine(userDir, rel);
            if (!File.Exists(descriptorPath))
            {
                progress?.Report($"  [missing descriptor] {rel}");
                mods.Add(new ModSnapshot(key, Path.GetFileNameWithoutExtension(rel), rel, null, null, null, "", order, []));
                continue;
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
                    cache, ct);
            }
            else if (File.Exists(content) && content.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                files = HashZipEntries(content);
            }
            else
            {
                progress?.Report($"  [missing content] {name} -> {content}");
                files = [];
            }

            progress?.Report($"  {order,3}. {name} ({files.Count} files)");
            mods.Add(new ModSnapshot(key, name, rel, d.RemoteFileId, d.Version, d.SupportedVersion, content, order, files));
        }

        return new MachineSnapshot(machineName, gameVersion, gameDir, DateTime.UtcNow, baseSnap, dlcs, mods);
    }

    static async Task<List<ModFile>> HashAllAsync(IEnumerable<(string Rel, string Abs)> files, HashCache cache, CancellationToken ct)
    {
        var bag = new ConcurrentBag<ModFile>();
        await Parallel.ForEachAsync(files, ct, (f, _) =>
        {
            var md5 = cache.GetMd5(f.Abs, out var size);
            bag.Add(new ModFile(f.Rel, md5, size));
            return ValueTask.CompletedTask;
        });
        return Sorted(bag);
    }

    static List<ModFile> HashZipEntries(string zipPath)
    {
        var files = new List<ModFile>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            using var s = e.Open();
            files.Add(new ModFile(e.FullName, Convert.ToHexStringLower(MD5.HashData(s)), e.Length));
        }
        return Sorted(files);
    }

    static List<ModSnapshot> ScanDlcs(string gameDir, DlcLoad load, IProgress<string>? progress)
    {
        var disabled = new HashSet<string>(load.DisabledDlcs.Select(d => d.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        var dlcs = new List<ModSnapshot>();
        var dlcRoot = Path.Combine(gameDir, "dlc");
        if (!Directory.Exists(dlcRoot)) return dlcs;

        int order = 0;
        foreach (var descPath in Directory.EnumerateFiles(dlcRoot, "*.dlc", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var descRel = Path.GetRelativePath(gameDir, descPath).Replace('\\', '/');
            if (disabled.Contains(descRel)) { progress?.Report($"  [disabled] {descRel}"); continue; }

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
                    if (!e.FullName.EndsWith('/')) files.Add(new ModFile(e.FullName, $"crc32:{e.Crc32:x8}", e.Length));
            }
            else progress?.Report($"  [missing archive] {name} -> {archive}");

            dlcs.Add(new ModSnapshot("dlc:" + Path.GetFileNameWithoutExtension(descPath), name, descRel,
                b.GetString("steam_id"), b.GetString("zip_checksum"), null, zipPath ?? "", ++order, Sorted(files)));
        }
        return dlcs;
    }

    static List<ModFile> Sorted(IEnumerable<ModFile> files)
    {
        var list = files.ToList();
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
        return list;
    }

    static string ReadGameVersion(string userDir, IProgress<string>? progress)
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
            progress?.Report($"  [warning] could not read game.log: {ex.Message}");
            return "unknown";
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter SnapshotScannerTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Port machine snapshot scanner with parallel cached hashing

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: ModDiffer

**Files:**
- Create: `FazStellarisModmanager.Core/Diff/ModDiffer.cs`
- Test: `FazStellarisModmanager.Tests/ModDifferTests.cs`

This replaces HashCoop's `ModDiff.Print` with structured output. "Target" means the host and "mine" means the local machine:
- **Missing:** the target has the unit and I don't.
- **Extra:** I have the unit and the target doesn't.

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core.Diff;
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Tests;

public class ModDifferTests
{
    static ModSnapshot Unit(string key, int order, params (string Path, string Md5)[] files) =>
        new(key, key.ToUpperInvariant(), $"mod/{key}.mod", null, null, null, "", order,
            files.Select(f => new ModFile(f.Path, f.Md5, 1)).ToList());

    static MachineSnapshot Machine(string version, params ModSnapshot[] mods) =>
        new("m", version, "", DateTime.UtcNow, Unit("base", 0, ("common/a.txt", "aa")), [Unit("dlc:dlc001", 1, ("x", "crc32:1"))], mods.ToList());

    [Fact]
    public void Identical_snapshots_match()
    {
        var a = Machine("v4.4", Unit("ugc:1", 1, ("f", "1")), Unit("ugc:2", 2, ("g", "2")));
        var b = Machine("v4.4", Unit("ugc:1", 1, ("f", "1")), Unit("ugc:2", 2, ("g", "2")));

        var r = ModDiffer.Diff(a, b);

        Assert.True(r.IsMatch);
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Reports_missing_and_extra_mods()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2));
        var mine = Machine("v4.4", Unit("ugc:1", 1), Unit("local:x.mod", 2));

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.IsMatch);
        Assert.Equal(new[] { ("ugc:1", UnitStatus.Ok), ("ugc:2", UnitStatus.Missing), ("local:x.mod", UnitStatus.Extra) },
            r.Mods.Select(m => (m.Key, m.Status)));
    }

    [Fact]
    public void Reports_content_mismatch_with_file_details()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("same", "s"), ("changed", "1"), ("only_target", "t")));
        var mine = Machine("v4.4", Unit("ugc:1", 1, ("same", "s"), ("CHANGED", "2"), ("only_mine", "m")));

        var mod = Assert.Single(ModDiffer.Diff(target, mine).Mods);

        Assert.Equal(UnitStatus.ContentMismatch, mod.Status);
        Assert.Equal(new[] { "changed" }, mod.Files!.Changed);
        Assert.Equal(new[] { "only_target" }, mod.Files.OnlyInTarget);
        Assert.Equal(new[] { "only_mine" }, mod.Files.OnlyInMine);
    }

    [Fact]
    public void Reports_load_order_mismatch()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("ugc:2", 1), Unit("ugc:1", 2), Unit("ugc:3", 3));

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.OrderMatches);
        Assert.False(r.IsMatch);
        Assert.Equal(new[] { true, true, false }, r.Mods.Select(m => m.OutOfOrder));
        Assert.All(r.Mods, m => Assert.Equal(UnitStatus.Ok, m.Status));
    }

    [Fact]
    public void Missing_mod_does_not_count_as_order_mismatch()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:2", 2), Unit("ugc:3", 3));
        var mine = Machine("v4.4", Unit("ugc:1", 1), Unit("ugc:3", 2));

        Assert.True(ModDiffer.Diff(target, mine).OrderMatches);
    }

    [Fact]
    public void Reports_game_version_base_and_dlc_differences()
    {
        var target = Machine("v4.4");
        var mine = Machine("v4.3") with
        {
            Base = Unit("base", 0, ("common/a.txt", "zz")),
            Dlcs = [],
        };

        var r = ModDiffer.Diff(target, mine);

        Assert.False(r.GameVersionMatches);
        Assert.Equal(new[] { "common/a.txt" }, r.BaseFiles.Changed);
        Assert.Equal(UnitStatus.Missing, Assert.Single(r.Dlcs).Status);
    }

    [Fact]
    public void Duplicate_keys_and_case_variant_paths_do_not_throw()
    {
        var target = Machine("v4.4", Unit("ugc:1", 1, ("a", "1"), ("A", "1")), Unit("ugc:1", 2));
        var mine = Machine("v4.4", Unit("UGC:1", 1, ("a", "1")));

        var r = ModDiffer.Diff(target, mine);

        Assert.Equal(UnitStatus.Ok, Assert.Single(r.Mods).Status);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModDifferTests`
Expected: build FAILS with `The type or namespace name 'Diff' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Diff/ModDiffer.cs`**

```csharp
using FazStellarisModmanager.Core.Snapshots;

namespace FazStellarisModmanager.Core.Diff;

public enum UnitStatus { Ok, Missing, Extra, ContentMismatch }

public sealed record FileDiff(List<string> Changed, List<string> OnlyInTarget, List<string> OnlyInMine)
{
    public bool IsEmpty => Changed.Count == 0 && OnlyInTarget.Count == 0 && OnlyInMine.Count == 0;
}

/// <summary>One DLC or mod compared between target (host) and mine. Orders are 1-based load positions.</summary>
public sealed record UnitDiff(
    string Key,
    string Name,
    string? RemoteId,
    UnitStatus Status,
    int? TargetOrder,
    int? MineOrder,
    bool OutOfOrder,
    string? TargetVersion,
    string? MineVersion,
    FileDiff? Files);

public sealed record DiffResult(string TargetGameVersion, string MineGameVersion, FileDiff BaseFiles, List<UnitDiff> Dlcs, List<UnitDiff> Mods)
{
    public bool GameVersionMatches => TargetGameVersion == MineGameVersion;
    public bool OrderMatches => !Mods.Any(m => m.OutOfOrder);

    public bool IsMatch => GameVersionMatches && BaseFiles.IsEmpty && OrderMatches
        && Dlcs.All(d => d.Status == UnitStatus.Ok) && Mods.All(m => m.Status == UnitStatus.Ok);
}

public static class ModDiffer
{
    static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    /// <summary>Compares my snapshot against the target (host). Result lists target units in load order, then my extras.</summary>
    public static DiffResult Diff(MachineSnapshot target, MachineSnapshot mine) =>
        new(target.GameVersion, mine.GameVersion,
            DiffFiles(target.Base.Files, mine.Base.Files),
            DiffUnits(target.Dlcs, mine.Dlcs, checkOrder: false),
            DiffUnits(target.Mods, mine.Mods, checkOrder: true));

    static List<UnitDiff> DiffUnits(List<ModSnapshot> target, List<ModSnapshot> mine, bool checkOrder)
    {
        var t = Index(target);
        var m = Index(mine);

        // Order is compared only over units both sides have, so a missing mod does not flag every later one.
        var outOfOrder = new HashSet<string>(Cmp);
        if (checkOrder)
        {
            var sharedT = t.Values.OrderBy(u => u.LoadOrder).Select(u => u.Key).Where(m.ContainsKey).ToList();
            var sharedM = m.Values.OrderBy(u => u.LoadOrder).Select(u => u.Key).Where(t.ContainsKey).ToList();
            for (int i = 0; i < sharedT.Count; i++)
                if (!Cmp.Equals(sharedT[i], sharedM[i])) outOfOrder.Add(sharedT[i]);
        }

        var result = new List<UnitDiff>();
        foreach (var x in t.Values.OrderBy(u => u.LoadOrder))
        {
            if (!m.TryGetValue(x.Key, out var y))
            {
                result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId, UnitStatus.Missing, x.LoadOrder, null, false, x.Version, null, null));
                continue;
            }
            var files = DiffFiles(x.Files, y.Files);
            result.Add(new UnitDiff(x.Key, x.Name, x.RemoteId,
                files.IsEmpty ? UnitStatus.Ok : UnitStatus.ContentMismatch,
                x.LoadOrder, y.LoadOrder, outOfOrder.Contains(x.Key), x.Version, y.Version,
                files.IsEmpty ? null : files));
        }
        foreach (var y in m.Values.Where(u => !t.ContainsKey(u.Key)).OrderBy(u => u.LoadOrder))
            result.Add(new UnitDiff(y.Key, y.Name, y.RemoteId, UnitStatus.Extra, null, y.LoadOrder, false, null, y.Version, null));
        return result;
    }

    public static FileDiff DiffFiles(List<ModFile> target, List<ModFile> mine)
    {
        var t = IndexFiles(target);
        var m = IndexFiles(mine);
        return new FileDiff(
            t.Keys.Where(p => m.TryGetValue(p, out var o) && o.Md5 != t[p].Md5).Order(Cmp).ToList(),
            t.Keys.Where(p => !m.ContainsKey(p)).Order(Cmp).ToList(),
            m.Keys.Where(p => !t.ContainsKey(p)).Order(Cmp).ToList());
    }

    // First occurrence wins on duplicate keys / case-variant paths, so malformed snapshots never throw.
    static Dictionary<string, ModSnapshot> Index(List<ModSnapshot> units)
    {
        var d = new Dictionary<string, ModSnapshot>(Cmp);
        foreach (var u in units) d.TryAdd(u.Key, u);
        return d;
    }

    static Dictionary<string, ModFile> IndexFiles(List<ModFile> files)
    {
        var d = new Dictionary<string, ModFile>(Cmp);
        foreach (var f in files) d.TryAdd(f.Path, f);
        return d;
    }
}
```

`Dictionary.Values` enumerates in insertion order when nothing has been removed, and the `OrderBy(LoadOrder)` makes the order explicit either way.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModDifferTests`
Expected: `Passed!  - Failed: 0, Passed: 7`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add structured mod differ

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: GameLauncher

**Files:**
- Create: `FazStellarisModmanager.Core/Game/GameLauncher.cs`
- Test: `FazStellarisModmanager.Tests/GameLauncherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class GameLauncherTests
{
    [Fact]
    public void Launch_throws_when_exe_missing()
    {
        using var tmp = new TempDir();

        var ex = Assert.Throws<FileNotFoundException>(() => GameLauncher.Launch(tmp.Path));

        Assert.Equal(Path.Combine(tmp.Path, "stellaris.exe"), ex.FileName);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test FazStellarisModmanager.Tests --filter GameLauncherTests`
Expected: build FAILS with `The name 'GameLauncher' does not exist`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/Game/GameLauncher.cs`**

```csharp
using System.Diagnostics;

namespace FazStellarisModmanager.Core.Game;

public static class GameLauncher
{
    public static string ExePath(string gameDir) => Path.Combine(gameDir, "stellaris.exe");

    public static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    /// <summary>Starts stellaris.exe directly (skipping the Paradox launcher); the game reads dlc_load.json on startup.</summary>
    public static void Launch(string gameDir)
    {
        var exe = ExePath(gameDir);
        if (!File.Exists(exe)) throw new FileNotFoundException("stellaris.exe not found in the game directory.", exe);
        using var process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = gameDir, UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start Stellaris.");
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test FazStellarisModmanager.Tests --filter GameLauncherTests`
Expected: `Passed!  - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add game launcher

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: ModManagerService

**Files:**
- Create: `FazStellarisModmanager.Core/ModManagerService.cs`
- Test: `FazStellarisModmanager.Tests/ModManagerServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FazStellarisModmanager.Core;
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class ModManagerServiceTests
{
    static ModManagerService Create(FakeInstall fake)
    {
        var paths = new AppPaths(fake.DataDir);
        SettingsStore.Save(paths.Settings, new AppSettings(UserDir: fake.UserDir));
        return new ModManagerService(paths, _ => fake.GameDir);
    }

    [Fact]
    public void Resolve_uses_settings_and_derives_workshop_dir()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        var p = svc.Resolve();

        Assert.Equal(fake.UserDir, p.UserDir);
        Assert.Equal(fake.GameDir, p.GameDir);
        Assert.Equal(fake.WorkshopDir, p.WorkshopDir);
    }

    [Fact]
    public void RefreshLibrary_creates_workshop_descriptors_and_scans()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        var lib = svc.RefreshLibrary();

        Assert.Equal(new[] { "local:local.mod", "ugc:111" }, lib.Select(m => m.Key));
        Assert.True(File.Exists(Path.Combine(fake.UserDir, "mod", "ugc_111.mod")));
    }

    [Fact]
    public void ImportCurrent_reads_dlc_load_with_library_names()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();

        var list = svc.ImportCurrent("Current");

        var entry = Assert.Single(list.Mods);
        Assert.Equal(("local:local.mod", "Local Mod"), (entry.Key, entry.Name));
        Assert.True(svc.IsInstalled(entry));
        Assert.False(svc.IsInstalled(new ModListEntry("ugc:9", "Nine", "mod/ugc_9.mod", "9")));
    }

    [Fact]
    public void Apply_writes_dlc_load_and_backs_up()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);
        svc.RefreshLibrary();
        var list = new ModList("L", [new("ugc:111", "Workshop One", "mod/ugc_111.mod", "111"), new("local:local.mod", "Local Mod", "mod/local.mod", null)], []);

        var backup = svc.Apply(list);

        Assert.Equal(new[] { "mod/ugc_111.mod", "mod/local.mod" }, DlcLoadFile.Read(fake.UserDir).EnabledMods);
        Assert.NotNull(backup);
        Assert.StartsWith(Path.Combine(fake.DataDir, "backups"), backup);
    }

    [Fact]
    public void UpdateSettings_persists()
    {
        using var fake = new FakeInstall();
        var svc = Create(fake);

        svc.UpdateSettings(svc.Settings with { PlayerName = "Faz" });

        Assert.Equal("Faz", new ModManagerService(new AppPaths(fake.DataDir), _ => fake.GameDir).Settings.PlayerName);
    }

    [Fact]
    public void Launch_without_game_dir_throws_helpful_error()
    {
        using var fake = new FakeInstall();
        var svc = new ModManagerService(new AppPaths(fake.DataDir), _ => null);

        var ex = Assert.Throws<InvalidOperationException>(svc.Launch);

        Assert.Contains("Settings", ex.Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FazStellarisModmanager.Tests --filter ModManagerServiceTests`
Expected: build FAILS with `The type or namespace name 'ModManagerService' could not be found`.

- [ ] **Step 3: Implement `FazStellarisModmanager.Core/ModManagerService.cs`**

```csharp
using FazStellarisModmanager.Core.Game;
using FazStellarisModmanager.Core.Library;
using FazStellarisModmanager.Core.Lists;
using FazStellarisModmanager.Core.Paths;

namespace FazStellarisModmanager.Core;

public sealed record ResolvedPaths(string UserDir, string? GameDir, string? WorkshopDir);

/// <summary>Stateful facade the UI talks to: settings, installed-mod library, saved lists, apply and launch.</summary>
public sealed class ModManagerService
{
    readonly AppPaths _paths;
    readonly Func<string?, string?> _findGameDir;

    /// <param name="findGameDir">Maps the configured game dir (or null) to a valid install; defaults to <see cref="GameLocator.FindGameDir"/>.</param>
    public ModManagerService(AppPaths paths, Func<string?, string?>? findGameDir = null)
    {
        _paths = paths;
        _findGameDir = findGameDir ?? (dir => GameLocator.FindGameDir(dir));
        Settings = SettingsStore.Load(paths.Settings);
        Lists = new ModListStore(paths.Lists);
    }

    public AppPaths Paths => _paths;
    public AppSettings Settings { get; private set; }
    public ModListStore Lists { get; }
    public IReadOnlyList<InstalledMod> Library { get; private set; } = [];

    public ResolvedPaths Resolve()
    {
        var user = string.IsNullOrWhiteSpace(Settings.UserDir) ? GameLocator.DefaultUserDir() : Settings.UserDir;
        var game = _findGameDir(Settings.GameDir);
        return new ResolvedPaths(user, game, game is null ? null : GameLocator.WorkshopDirFor(game));
    }

    public void UpdateSettings(AppSettings settings)
    {
        SettingsStore.Save(_paths.Settings, settings);
        Settings = settings;
    }

    /// <summary>Creates missing mod/ugc_&lt;id&gt;.mod descriptors for downloaded Workshop items, then rescans mod/*.mod.</summary>
    public IReadOnlyList<InstalledMod> RefreshLibrary()
    {
        var p = Resolve();
        if (p.WorkshopDir is not null) ModLibrary.EnsureWorkshopDescriptors(p.UserDir, p.WorkshopDir);
        Library = ModLibrary.Scan(p.UserDir);
        return Library;
    }

    public bool IsInstalled(ModListEntry entry) =>
        Library.Any(m => m.DescriptorRel.Equals(entry.DescriptorRel, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mods currently enabled in dlc_load.json, as a list.</summary>
    public ModList ImportCurrent(string name) => ModList.FromDlcLoad(name, DlcLoadFile.Read(Resolve().UserDir), Library);

    /// <summary>Writes the list to dlc_load.json after backing up the old file. Returns the backup path (null if there was none).</summary>
    public string? Apply(ModList list) => DlcLoadFile.Write(Resolve().UserDir, list.ToDlcLoad(), _paths.Backups);

    public void Launch()
    {
        var game = Resolve().GameDir
            ?? throw new InvalidOperationException("Stellaris install not found. Set the game folder in Settings.");
        GameLauncher.Launch(game);
    }
}
```

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test FazStellarisModmanager.Tests`
Expected: `Passed!  - Failed: 0, Passed: 53`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add ModManagerService facade

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Wire DI and the Settings page

**Files:**
- Create: `FazStellarisModmanager/AppServices.cs`, `FazStellarisModmanager/Pages/Settings.razor`
- Modify: `FazStellarisModmanager/MainWindow.xaml.cs`, `FazStellarisModmanager/_Imports.razor`

- [ ] **Step 1: Create `FazStellarisModmanager/AppServices.cs`**

```csharp
using FazStellarisModmanager.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FazStellarisModmanager;

public static class AppServices
{
    /// <summary>
    /// Registers app services. "--data-dir &lt;path&gt;" replaces %AppData%\FazStellarisModmanager,
    /// which lets two instances run side by side with different settings (used for session testing).
    /// </summary>
    public static void Register(IServiceCollection services, string[] args)
    {
        var dataDir = ArgValue(args, "--data-dir");
        var paths = dataDir is null ? AppPaths.Default() : new AppPaths(Path.GetFullPath(dataDir));
        services.AddSingleton(paths);
        services.AddSingleton(_ => new ModManagerService(paths));
    }

    static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
```

- [ ] **Step 2: Register services in `FazStellarisModmanager/MainWindow.xaml.cs`**

Replace the constructor body so that it reads:

```csharp
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        AppServices.Register(services, Environment.GetCommandLineArgs());
        Resources.Add("services", services.BuildServiceProvider());
        InitializeComponent();
    }
```

- [ ] **Step 3: Add the Core namespaces to `FazStellarisModmanager/_Imports.razor`**

```razor
@using System.IO
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using FazStellarisModmanager
@using FazStellarisModmanager.Core
@using FazStellarisModmanager.Core.Game
@using FazStellarisModmanager.Core.Library
@using FazStellarisModmanager.Core.Lists
```

- [ ] **Step 4: Create `FazStellarisModmanager/Pages/Settings.razor`**

```razor
@page "/settings"
@inject ModManagerService Manager

<h1>Settings</h1>

<div class="form">
    <label>
        Stellaris install folder (leave empty to auto-detect)
        <input @bind="gameDir" placeholder="@(detected.GameDir ?? "not found — enter the folder containing stellaris.exe")" />
    </label>
    <label>
        Stellaris documents folder (contains dlc_load.json; leave empty for default)
        <input @bind="userDir" placeholder="@detected.UserDir" />
    </label>
    <label>
        Player name (shown to other players in sessions)
        <input @bind="playerName" placeholder="@Environment.MachineName" />
    </label>
    <div><button class="primary" @onclick="Save">Save</button></div>
</div>

<p class="status">
    Game: @(detected.GameDir ?? "not found")<br />
    Workshop: @(detected.WorkshopDir ?? "-")<br />
    App data: @Manager.Paths.Root
</p>
@if (message is not null)
{
    <p class="status @(error ? "error" : "")">@message</p>
}

@code {
    string? gameDir, userDir, playerName, message;
    bool error;
    ResolvedPaths detected = new("", null, null);

    protected override void OnInitialized()
    {
        (gameDir, userDir, playerName) = (Manager.Settings.GameDir, Manager.Settings.UserDir, Manager.Settings.PlayerName);
        detected = Manager.Resolve();
    }

    void Save()
    {
        try
        {
            Manager.UpdateSettings(new AppSettings(Blank(gameDir), Blank(userDir), Blank(playerName)));
            detected = Manager.Resolve();
            error = detected.GameDir is null;
            message = error ? "Saved, but no Stellaris install was found at that folder." : "Saved.";
        }
        catch (IOException ex)
        {
            (error, message) = (true, ex.Message);
        }
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

- [ ] **Step 5: Build and check the page manually**

Run: `dotnet build FazStellarisModmanager.sln`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet run --project FazStellarisModmanager`
Expected: on the Settings tab, "Game:" shows your Stellaris install folder (auto-detected from Steam) and "Workshop:" shows `...\steamapps\workshop\content\281990`. Enter a player name and click Save; it should say "Saved.". Then check that `%AppData%\FazStellarisModmanager\settings.json` contains the name.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Wire DI and add Settings page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Mods page (library, list editor, apply and launch)

**Files:**
- Replace: `FazStellarisModmanager/Pages/Mods.razor`

- [ ] **Step 1: Replace `FazStellarisModmanager/Pages/Mods.razor`**

```razor
@page "/"
@inject ModManagerService Manager

<div class="mods-page">
    <section class="panel">
        <header>
            <h2>Installed (@Manager.Library.Count)</h2>
            <input class="search" placeholder="Search…" @bind="filter" @bind:event="oninput" />
            <button @onclick="Refresh">Rescan</button>
        </header>
        <ul class="mod-list">
            @foreach (var m in FilteredLibrary)
            {
                var inList = current.Any(e => SameKey(e.Key, m.Key));
                <li class="@(inList ? "in-list" : "")">
                    <span class="badge @m.Source.ToString().ToLowerInvariant()">@(m.Source == ModSource.Workshop ? "Workshop" : "Local")</span>
                    <span class="name" title="@m.DescriptorRel">@m.Name</span>
                    @if (!inList)
                    {
                        <button class="small" @onclick="() => Add(m)">Add →</button>
                    }
                </li>
            }
        </ul>
    </section>

    <section class="panel">
        <header>
            <h2>List</h2>
            <input class="list-name" @bind="listName" placeholder="List name" />
            <select @onchange="LoadList">
                <option value="">Load list…</option>
                @foreach (var l in savedLists)
                {
                    <option value="@l.Name">@l.Name</option>
                }
            </select>
        </header>
        <div class="toolbar">
            <button @onclick="ImportCurrent">Import current</button>
            <button @onclick="Save">Save list</button>
            <button class="danger" @onclick="DeleteList" disabled="@(!savedLists.Any(l => l.Name == listName.Trim()))">Delete list</button>
            <button class="primary" @onclick="Apply">Apply</button>
            <button class="primary" @onclick="ApplyAndLaunch">Apply &amp; Launch</button>
        </div>
        <ol class="mod-list ordered">
            @for (int i = 0; i < current.Count; i++)
            {
                var index = i;
                var e = current[i];
                var installed = Manager.IsInstalled(e);
                <li draggable="true" class="@(installed ? "" : "missing")"
                    @ondragstart="() => dragIndex = index" @ondragover:preventDefault @ondrop="() => Drop(index)">
                    <span class="order">@(index + 1)</span>
                    <span class="name" title="@e.DescriptorRel">@e.Name</span>
                    @if (!installed)
                    {
                        <span class="badge missing">not installed</span>
                    }
                    <button class="small" @onclick="() => Move(index, -1)" disabled="@(index == 0)">↑</button>
                    <button class="small" @onclick="() => Move(index, 1)" disabled="@(index == current.Count - 1)">↓</button>
                    <button class="small danger" @onclick="() => current.RemoveAt(index)">✕</button>
                </li>
            }
        </ol>
    </section>
</div>

@if (status is not null)
{
    <p class="status @(error ? "error" : "")">@status</p>
}

@code {
    List<ModListEntry> current = [];
    List<string> disabledDlcs = [];
    string listName = "Current";
    string filter = "";
    int? dragIndex;
    IReadOnlyList<ModList> savedLists = [];
    string? status;
    bool error;

    IEnumerable<InstalledMod> FilteredLibrary =>
        Manager.Library.Where(m => filter.Length == 0 || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

    static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    protected override void OnInitialized() => Run(() =>
    {
        Manager.RefreshLibrary();
        savedLists = Manager.Lists.LoadAll();
        SetList(Manager.ImportCurrent("Current"));
        return $"Loaded {current.Count} enabled mods from dlc_load.json.";
    });

    void Refresh() => Run(() => $"Found {Manager.RefreshLibrary().Count} installed mods.");

    void Add(InstalledMod m) => current.Add(new ModListEntry(m.Key, m.Name, m.DescriptorRel, m.RemoteId));

    void Move(int index, int delta)
    {
        var e = current[index];
        current.RemoveAt(index);
        current.Insert(index + delta, e);
    }

    void Drop(int target)
    {
        if (dragIndex is not int from || from == target) return;
        var e = current[from];
        current.RemoveAt(from);
        current.Insert(target, e);
        dragIndex = null;
    }

    ModList Build() => new(listName.Trim(), current.ToList(), disabledDlcs.ToList());

    void SetList(ModList list)
    {
        listName = list.Name;
        current = list.Mods.ToList();
        disabledDlcs = list.DisabledDlcs.ToList();
    }

    void ImportCurrent() => Run(() =>
    {
        SetList(Manager.ImportCurrent("Current"));
        return $"Imported {current.Count} mods from dlc_load.json.";
    });

    void Save() => Run(() =>
    {
        Manager.Lists.Save(Build());
        savedLists = Manager.Lists.LoadAll();
        return $"Saved list '{listName.Trim()}'.";
    });

    void DeleteList() => Run(() =>
    {
        Manager.Lists.Delete(listName);
        savedLists = Manager.Lists.LoadAll();
        return $"Deleted list '{listName.Trim()}'.";
    });

    void LoadList(ChangeEventArgs e) => Run(() =>
    {
        if (e.Value is not string name || name.Length == 0) return null;
        SetList(Manager.Lists.Load(name) ?? throw new InvalidOperationException($"List '{name}' not found."));
        return $"Loaded list '{name}'.";
    });

    void Apply() => Run(() =>
    {
        var backup = Manager.Apply(Build());
        return $"Applied {current.Count} mods to dlc_load.json" + (backup is null ? "." : $" (backup: {Path.GetFileName(backup)}).");
    });

    void ApplyAndLaunch() => Run(() =>
    {
        var steamWarning = GameLauncher.IsSteamRunning() ? "" : " Warning: Steam does not appear to be running.";
        Manager.Apply(Build());
        Manager.Launch();
        return "Applied and launched Stellaris." + steamWarning;
    });

    void Run(Func<string?> action)
    {
        try
        {
            var message = action();
            if (message is not null) (status, error) = (message, false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException
                                   or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            (status, error) = (ex.Message, true);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build FazStellarisModmanager.sln`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Manual end-to-end check**

Run: `dotnet run --project FazStellarisModmanager`

Check each of these:
1. The Mods tab loads. "Installed (N)" lists your mods with Workshop/Local badges. The right panel shows your currently enabled mods in `dlc_load.json` order, with the status line "Loaded N enabled mods…".
2. Typing in Search filters the left list.
3. **Save list** saves under the name "Test", and the list appears in "Load list…".
4. Change the order with ↑/↓ and by dragging, remove one mod, and add one from the left.
5. Click **Apply**. The status shows a backup file name. Open `Documents\Paradox Interactive\Stellaris\dlc_load.json` and confirm the new order. Confirm the backup exists in `%AppData%\FazStellarisModmanager\backups`.
6. Pick "Test" from "Load list…" and the original order comes back. Click **Apply** to restore it.
7. With Steam running, click **Apply & Launch**. Stellaris should start without the Paradox launcher, and the in-game mod count should match the list.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test FazStellarisModmanager.Tests`
Expected: `Passed!  - Failed: 0, Passed: 53`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Mods page: library, list editor, apply and launch

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Follow-on plans (written once this one is done)

- `docs/superpowers/plans/<date>-multiplayer-sync.md`: Session protocol, SessionHost/SessionClient and the Session tab. It uses `SnapshotScanner`, `ModDiffer` and `ModManagerService` from this plan.
- `docs/superpowers/plans/<date>-workshop-install.md`: the `FazStellarisModmanager.Steam` project (Steamworks.NET), `IWorkshopService`, and the Match host flow. It uses `ModKeys.WorkshopId` and `ModManagerService.RefreshLibrary`.
- The tech tree gets its own brainstorm and spec first.

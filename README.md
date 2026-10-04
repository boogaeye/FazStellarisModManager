# Faz Stellaris Mod Manager

A Windows desktop mod manager for **Stellaris**, built for playing multiplayer with friends.

## Features

- **Mod lists:** save named lists in load order, import the current `dlc_load.json`, apply a list, and launch the game directly. Every write to `dlc_load.json` is backed up first.
- **Multiplayer sync:** one player hosts a session over direct IP (TCP) and the others join. Everyone sees which mods they are missing, have extra, or have in a different version or order than the host. **Match host** fixes the difference.
- **Tech tree:**
  - every technology of a mod list, after the game's override rules, with icons;
  - what each tech unlocks;
  - stat bonuses, including empire-dependent job names (e.g. Bureaucrats / Priests / Managers) with their conditions;
  - research weights, conditions and the raw definition.
- **Whole-tree overview:** a zoomable map of all techs, with research route planning to one or more targets. You can mark techs as researched; marks are saved per mod list.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build), or the .NET 10 Desktop Runtime (to run a build)
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)
- Stellaris (Steam)

## Build and run

```bash
dotnet build FazStellarisModmanager.sln -c Release
dotnet run --project FazStellarisModmanager -c Release
```

Run the tests:

```bash
dotnet test FazStellarisModmanager.Tests
```

The game folder and the Stellaris user folder (`Documents\Paradox Interactive\Stellaris`) are detected automatically. You can override them in **Settings**. App data (mod lists, backups, caches, research progress) lives in `%AppData%\FazStellarisModmanager`.

## Installing

Download `FazStellarisModManager-win-x64.zip` from the [latest release](https://github.com/boogaeye/FazStellarisModManager/releases/latest). Unzip it into a folder you own, such as `Documents\FazStellarisModManager`; avoid `Program Files`, where the app can't update itself. Then run `FazStellarisModmanager.exe`. No .NET install is needed.

At startup the app checks for a newer release and asks before updating; you can say **Not now**. Turn the check off, or check by hand, in **Settings → Updates**.

## Releasing a new version

Tag the commit with the new version and push the tag:

```bash
git tag v0.2.0
git push origin v0.2.0
```

The **Release** workflow tests the code, builds a self-contained Windows x64 app stamped with that version, and publishes it as a GitHub Release with `FazStellarisModManager-win-x64.zip` attached. Installed copies offer the update the next time they start.

## Project layout

| Project | Purpose |
|---|---|
| `FazStellarisModmanager` | WPF + Blazor Hybrid desktop app (UI) |
| `FazStellarisModmanager.Core` | Game paths, mod descriptors, mod lists, hashing and diffing, session protocol, tech tree parsing |
| `FazStellarisModmanager.Tests` | xUnit tests for Core |

Design specs and implementation plans are in `docs/superpowers/`.

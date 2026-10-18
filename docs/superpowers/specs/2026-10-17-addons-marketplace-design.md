# Addons Tab and Marketplace Addon: Design (Sub-project 24)

Approved mockup: `.superpowers/brainstorm/497-1792275941/content/addons-marketplace-v2.html`. It shows the editable view and the read-only view a player sees while connected to a host. Real icons and values came from the user's list.

## Concept
- **Addons** are features the app builds into one generated mod, **Faz Addons**.
- **Not a mod in the manager:** it isn't in `ModList`, can't be moved or removed, and is hidden from the library, the Add mods picker, Conflicts and the Tech/Events sources.
- **Always last:** when addons are on, Apply appends it after every mod, in `dlc_load.json` and in the launcher playset.
- **Deterministic:** the same addon settings plus the same mod files give byte-identical output. In a session, every player builds it from the **host's** settings, and the normal mod comparison confirms the match. That also keeps the multiplayer checksum equal.
- **Version 1** has one addon, the **Marketplace**.

## Facts from the game (verified on the user's install, Stellaris 4.5.2)
- **Resource definitions** are in `common/strategic_resources/*.txt`. A resource is on the Galactic Market when both `market_amount` (the bundle size) and `market_price` are positive (the default is -1).
  - `tradable = yes/no` controls trade deals between empires.
  - `max` is the **base** storage. Modifiers (`country_resource_max_*`, silos, techs) add to it, unless the resource has `fixed_max_amount = yes`.
  - `visibility_prerequisite = { always = no }` marks hidden internal resources (e.g. `ai_unobtainium`).
  - `tradable_in_market = { … }` is a per-country gate (e.g. consumer goods for some gestalts).
- **`strategic_resources` doesn't let a single definition be overridden** (a duplicate key in another file loads twice and breaks). To change a resource, you replace **the whole file** that defines it, by shipping a file with the same name in a mod that loads later.
- **Max per trade** = `market_amount × NEconomy.MARKET_MAX_BASE_AMOUNTS` (200 in the base game). That gives basic 100×200 = 20,000 and alloys 25×200 = 5,000.
- **Other market settings in `NEconomy`:**
  - `MARKET_TRADE_FEE_BASE` (0.3) and `MARKET_TRADE_FEE_MIN` (0.05);
  - `MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE` (-80) and `MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE` (400).
- **`NAI.AI_ALLOWED_TO_BUY` / `AI_ALLOWED_TO_SELL`** are resource lists the AI may trade. The base game lists 11 resources, so mod resources are never AI-traded.
- **Defines** override per key: a later definition of `NEconomy = { KEY = value }` wins.
- **The Galactic Market keeps its own list of traded resources** in the save (`market.galactic_market_resources`). The game adds its own resources with `enable_on_market = <key>`: once when the market is founded (`action.104`) and then monthly (`action.116` on `on_monthly_pulse`). Resources the addon puts on the market only reach the Galactic Market through the same effect, so the addon adds `events/faz_addons_market.txt` (a hidden global event `faz_addons.1` that runs `enable_on_market` for each of them once the market is founded) and `common/on_actions/zzzzz_faz_addons.txt` (runs it `on_monthly_pulse`). The internal market needs nothing extra. Found while testing on the user's save, where dark energy and quasi-negative mass were missing from the Galactic Market. Follow-up (user request): `MarketplaceSettings.GalacticMarketNeedsStock` (default true) makes the event follow the game's own rule from `action.116`: a resource is added once `any_playable_country` that `is_galactic_community_member` has `resource_stockpile_compare >= 1` of it (all qualifying resources at once, not one per month). Off adds them right away. Protocol 6.
- **Not possible:** fallen empires hiding alloys from the trade screen is built into the game. There's no setting or file for it, so the addon doesn't offer it.

## Data model (`Core/Addons/`)
```csharp
public sealed record AddonSettings(bool Enabled = false, MarketplaceSettings? Marketplace = null);
public sealed record MarketplaceSettings(
    bool Enabled = true,
    int? MaxBaseAmounts = null,          // NEconomy.MARKET_MAX_BASE_AMOUNTS
    double? TradeFeeBase = null,         // NEconomy.MARKET_TRADE_FEE_BASE
    double? TradeFeeMin = null,          // NEconomy.MARKET_TRADE_FEE_MIN
    int? MinFluctuation = null,          // NEconomy.MARKET_MIN_FLUCTUATION_FROM_BASE_PRICE
    int? MaxFluctuation = null,          // NEconomy.MARKET_MAX_FLUCTUATION_FROM_BASE_PRICE
    IReadOnlyDictionary<string, ResourceOverride>? Resources = null);
public sealed record ResourceOverride(bool? OnMarket = null, int? MarketAmount = null, int? MarketPrice = null,
    bool? Tradable = null, bool? AiTrades = null, int? BaseStorage = null);
```
- **Null means unchanged.** An override equal to the game value is dropped when saved, so "Changed" counts real changes.
- **Storage:** `AddonSettingsStore` keeps it in `%AppData%\FazStellarisModmanager\addons.json`. A missing or corrupt file means the defaults (off).

## Reading the current game (`MarketCatalog`)
`MarketCatalog.Build(IReadOnlyList<ContentSource> sources, Localisation loc, SpriteIndex sprites, IconCache icons)` uses the sources of the current list: base game, then mods in load order, **never Faz Addons**.
- **Winning files:** for each file name in `common/strategic_resources`, the copy from the **last** source that has that relative path (case-insensitive).
- **Resources:** every top-level block in the winning files. Parse each with `ParadoxScriptParser` (comments and quotes respected) to get `ResourceInfo`:
  - Key, Name (localisation of the key, else the key), Category;
  - MarketAmount, MarketPrice, OnMarket (both > 0), Tradable;
  - Max (null if absent), FixedMax;
  - Hidden (`visibility_prerequisite` with `always = no`), Gated (`tradable_in_market` present);
  - SourceName (the mod), FileName;
  - IconUri: sprite `GFX_resource_<key>` → its texture through `IconCache.DataUri`, falling back to `gfx/interface/icons/resources/<key>.dds`; null if neither decodes.
  - A key defined in several winning files keeps the last one and lists all its files (`Files`).
- **Defines:** read all `common/defines/*.txt` of the sources in load order, files by name within a source; the last value per `Namespace.Key` wins. The catalog keeps `MaxBaseAmounts`, `TradeFeeBase`, `TradeFeeMin`, `MinFluctuation`, `MaxFluctuation`, `AiAllowedToBuy` and `AiAllowedToSell` (lists).
- **Service:** `AddonService` (a singleton in the app) builds the catalog off the UI thread for the current list (`TechSources.Build` with the edited list). It caches by a stamp of the list (descriptor paths in order) and invalidates on library refresh. It exposes `Catalog`, `IsBuilding`, `Progress` and `Changed`.

## Building the addon (`AddonBuilder`)
`AddonBuilder.Build(MarketCatalog catalog, IReadOnlyList<ContentSource> sources, AddonSettings settings, string gameVersion)` returns `AddonOutput` (an ordered list of relative path → bytes). It is pure: no file system writes.
- **Off:** if `!settings.Enabled`, or the Marketplace is off or has no changes, the output still contains the descriptor and an empty `README.txt`. The caller then doesn't append the mod at all (see Apply).
- **Strategic resources:** for each winning file containing a resource with a resource-level change, output `common/strategic_resources/<same file name>`:
  - the winning file's **original bytes**, edited only inside the changed blocks;
  - **set a key:** replace the value token of an existing top-level key in the block, else insert `\n\t<key> = <value>` right after the block's `{`;
  - **OnMarket true:** set `market_amount` (the override, else the existing positive value, else the category suggestion: basic 100, advanced 25, strategic 10, rare 5, else 10) and `market_price` (override, else existing positive, else 100);
  - **OnMarket false:** set `market_amount = -1`;
  - **Tradable:** set `tradable = yes|no`;
  - **BaseStorage:** set `max = N`. Only allowed when the resource already has `max`, and not when `FixedMax`; the UI disables it otherwise;
  - files with no changed resource are not output.
- **Defines:** output `common/defines/zzzzz_faz_addons.txt` when any define or AI list changes:
  - `NEconomy = { … }` with only the changed keys, in a fixed order;
  - `NAI = { AI_ALLOWED_TO_BUY = { "a" "b" … } AI_ALLOWED_TO_SELL = { … } }` when any AiTrades override exists. Each list is the catalog's current list with the AiTrades true/false applied, keeping the current order and appending added keys in ordinal order;
  - doubles are written with invariant culture and up to 4 decimals.
- **Descriptor:** `descriptor.mod` contains `name="Faz Addons"`, `version="1"`, `supported_version="v<major>.<minor>.*"` (from the game version, else `"*"`), `tags={ "Utilities" }`.
- **Line endings:** inserted text uses the file's own line ending, detected from the first line break.
- **Encoding:** a UTF-8 BOM is kept if the original had one; generated files have no BOM.

## Writing and applying
- **Fixed names:** descriptor `mod/faz_addons.mod` (`AddonMod.DescriptorRel`), content folder `mod/faz_addons/`, and `path="mod/faz_addons"` in the descriptor.
- **`AddonMod.Write(userDir, AddonOutput)`:**
  - creates or replaces the folder's files atomically;
  - deletes files in `mod/faz_addons/` that aren't in the output;
  - writes `mod/faz_addons.mod` with the descriptor content plus the `path=` line.
- **`ModManagerService.Apply(list)`, with an `AddonService`/`AddonSettings` provider injected as a `Func<AddonSettings?>` for the *effective* settings:**
  - When the effective settings are on and have changes: build the catalog for `list` (synchronously in Apply; the cost is small), build, write, and append `mod/faz_addons.mod` as the **last** entry of `enabled_mods` and of the launcher playset (`LauncherPlaysetFile.Write` gets the full list including it).
  - Otherwise don't append it. The folder may stay on disk.
  - `ApplyResult` gains `bool AddonsApplied`, and the Apply messages mention "with Faz Addons".
- **Hidden from the manager:**
  - `ModManagerService.Library` excludes `AddonMod.DescriptorRel`;
  - `ModList.FromDlcLoad` / `ImportCurrent` drop it;
  - `DupeConflicts` never sees it, since it isn't in the library;
  - the Conflicts page and `TechSources` use lists, so it never appears there.
- **Mods tab:** when the effective addon settings are on, a fixed last row `🔒 Faz Addons · generated · Marketplace · always last` sits after the list. It has no drag handle, position box or remove button.

## Sessions
- **Messages:** `Welcome` and `HostTargetUpdate` gain `AddonSettings? Addons`, the host's settings or null when off. The protocol goes from 3 to 4.
- **Host:** sends its saved settings. When the host clicks Apply on the Addons page, it rescans and pushes a new target (the existing `RescanAsync` path), so players see the new settings and the addon unit in the host snapshot.
- **Player:**
  - `SessionService.HostAddons` holds the host's settings.
  - **Effective settings:** the host's while connected as a client, otherwise the player's own saved settings, which are never overwritten.
  - **Match host:** Apply uses the effective settings, so the addon is built from the host's settings. The rescan then compares the addon unit like any mod.
  - **Comparison:** the addon unit (key `local:faz_addons.mod`) is labelled "Faz Addons". When it is Missing or Different, the comparison shows "Click Match host to rebuild Faz Addons from the host's settings". The MatchPlan treats it as handled by Apply, not as a manual install: it is excluded from `NeedsManualInstall`/`DiffersLocally`.

## Addons page (`/addons`, nav item after Live Game)
The page follows the mockup.
- **Header:**
  - the master switch;
  - the status: "Built as Faz Addons · loads after every mod · N files", plus a chip: "✔ Up to date" when the files on disk equal a fresh build, "Needs Apply" otherwise, or "✔ Matches the host" for a player in a session;
  - **Preview files:** a modal listing the output paths with each file's text;
  - **Apply:** applies the Mods tab's edited list (`EditedModList`), then if in a session, `Session.RescanAsync`.
- **Left:** the addon list. Marketplace has its own switch and a change count.
- **Market rules card:** three settings with the game's current values and live previews:
  - **Max per trade:** a number input plus slider from 50 to 5000. The preview reads "N bundles → Basic X · Alloys Y · Strategic Z", using the catalog's bundle sizes;
  - **Trade fee:** base and minimum, in %;
  - **Price swing:** minimum and maximum, in %.
- **Resources card:**
  - **Filter chips:** All, On market, Off market, Changed, Show hidden (hidden ones are left out unless it's on). Plus a search over name and key.
  - **Rows** are grouped by category (Basic, Advanced, Strategic, Rare, Other):
    - the icon, name, key, and tags (added / rule / hidden);
    - **From** (the mod);
    - switches for **Market**, **Trade deals** (`tradable`) and **AI trades** (the AI lists);
    - inputs for **Bundle** and **Base price** (disabled when off the market);
    - **Max per trade** (computed);
    - **Base storage** ("—" when the resource has no `max`, disabled when `FixedMax`).
  - Changed values have a yellow outline, and each row has a reset that removes its override.
  - Note: "Base storage is the starting capacity; silos, techs and other modifiers still add on top."
- **Read-only:** while connected as a client, there's the banner "🔒 You're connected to HOST. These are the host's addons: highlighted values are what they changed. Leave the session to edit your own." Every control is disabled and the host's settings are shown.
- **Saving:** edits save immediately to `addons.json`; nothing is written into the game until Apply.

## Testing
- **`MarketCatalog`:**
  - a synthetic base game plus a mod overriding the same file name: the mod's copy wins;
  - market, off-market, hidden, gated, max and fixed are read;
  - defines read last-wins, including the AI lists.
- **`AddonBuilder`:**
  - **resource edits:** replace an existing key, insert a missing key, OnMarket true with the category suggestion, OnMarket false → -1, tradable, max;
  - **file handling:** the rest of the file is byte-identical, unchanged files aren't output, and CRLF/BOM are kept;
  - **defines:** only changed keys, and the AI lists keep their order with additions appended and removals dropped;
  - **determinism:** two builds give equal bytes, and dictionary order doesn't matter.
- **`AddonMod.Write`:** writes the descriptor with `path`, removes stale files, and leaves files outside the folder alone.
- **`ModManagerService`:**
  - Apply with addons on appends `mod/faz_addons.mod` last in dlc_load and the playset;
  - Apply with addons off doesn't;
  - the library and `ImportCurrent` exclude it.
- **`AddonSettingsStore`:** a round trip, and corrupt or missing files give the defaults.
- **Session (loopback):**
  - the host's settings reach the player (`HostAddons`);
  - the player's Match host builds an identical addon, and its rescan diff shows the addon unit Ok;
  - leaving restores the player's own effective settings;
  - protocol 4.
- **Real data:** build the catalog for the user's list. Check the 56 resources, the 17 on the market, the winning files and the defines. Then build with the mockup's changes and inspect the generated files.

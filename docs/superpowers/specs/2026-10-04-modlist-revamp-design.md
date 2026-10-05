# Mod List Revamp — Design (Sub-project 11)

## Goal
The Mods page becomes a single list editor:
- Each row shows the mod's **icon**, name and badges.
- Rows can be reordered by **drag and drop** or by **typing a position number**.
- Workshop mods get a **Workshop ↗** link to their Steam page.

The old left-hand "Installed" panel is removed. Mods are added through an **Add mods** picker pop-up.

Mockup (approved, option A): `.superpowers/brainstorm/1450-1791160399/content/modlist.html` in the `FazStellarisModmanager-modlist` worktree.

## Facts
- **Descriptors:** mod descriptors have `picture="thumbnail.png"` (182 of the user's descriptors). Some use `thumbnail.jpg`, `cover.jpg` or other names.
- **Workshop folders:** 269 of 332 contain `thumbnail.png`/`.jpg` (a few spell it `Thumbnail.png` or `thumbnail.PNG`). Typical images are 400–960 px squares of about 360 KB.
- **Content location:** `InstalledMod.ContentPath` is the content folder, or the zip archive for `archive=` mods. Workshop zip mods keep the thumbnail next to the zip.
- **Existing page:** `Pages/Mods.razor` already has HTML5 drag events, ↑/↓ buttons, save/load/import/delete/apply/launch, and the library-errors list.
- **Core:** Core is `net10.0` with no image decoding. The app is WPF and can decode PNG, JPG, BMP and GIF with `System.Windows.Media.Imaging`.

## Decisions

### List editing (`Core/Lists/ModListEditor`, pure)
- **`MoveTo(list, fromIndex, position)`:** the 1-based position is clamped to 1..Count. It returns the new index.
- **`Move(list, fromIndex, toIndex)`:** the dragged mod takes the target's place. It goes after the target when moving down and before it when moving up.
- **`AddRange(list, mods)`:** appends installed mods not already in the list (by key, case-insensitive) in the given order and returns the number added.

### Thumbnails (`Core/Library/ModThumbnail`)
- **New field:** `InstalledMod` gains `string? Picture = null` (the descriptor's `picture`), filled by `ModLibrary.Scan`.
- **`Find(mod)`:** returns a `ThumbnailSource(FilePath, ZipEntry?)` or null. It looks in this order:
  1. In the content folder: the descriptor's `picture`, if it is a safe relative name (not rooted, no `..`, no `:`), then `thumbnail.png`, `thumbnail.jpg`, `thumbnail.jpeg`. Names match case-insensitively.
  2. For a `.zip` content file: the same names as root entries of the zip, then the same files in the zip's folder.
- **Size limit:** files or entries that are empty or larger than 20 MB are ignored.
- **`Read(source)`:** returns the bytes.
- **`Stamp(source)`:** returns the file's `size:lastWriteTicks`.

### Icon cache (`Core/Library/ModIconCache`)
- **Constructor:** `ModIconCache(string directory, Func<byte[], byte[]?> shrinkToPng)`. The app passes a WPF shrinker that scales the longer side to at most 64 px and encodes as PNG.
- **`GetAsync(mod)`:** runs on the thread pool.
  1. Find the thumbnail.
  2. Look up the cache file `<sha1(source id | stamp)>.png` in the directory.
  3. If it isn't there: read the thumbnail, shrink it, and write the result atomically.
  4. Return a `data:image/png;base64,…` URI, or null when anything fails.
- **Memo:** results are remembered per run by descriptor and content path.
- **`TryPeek(mod)`:** returns a completed result, otherwise null. It does no IO and is used while rendering.
- **App wiring:** the app registers the cache at `%AppData%\FazStellarisModmanager\mod-icons` (`AppPaths.ModIcons`). The shrinker lives in `FazStellarisModmanager/Services/ImageShrinker.cs`.

### Mods page (`Pages/Mods.razor`)
- **Toolbar row 1:** list name, Load list, Save list, Import current, Delete list, **Rescan mods**, then Apply and Apply & Launch on the right.
- **Toolbar row 2:** **+ Add mods**, a "Filter this list…" box (by name) and the mod count.
- **Status and library errors:** these stay as they are, under the toolbars.
- **Rows** (`<ol class="modlist">`, one row per entry; the filter hides rows but keeps their real positions):
  - ⋮⋮ grip;
  - position `<input type="number">`, applied on change (Enter or blur); a non-number shows an error status;
  - `ModIcon` (40 px, or a letter placeholder);
  - name, with the descriptor file underneath;
  - badges: Workshop or Local, "local copy of Workshop <id>" for local mods that have a Workshop id, and "not installed" in red;
  - **Workshop ↗**, a link to `steam://url/CommunityFilePage/<id>`, whenever a Workshop id is known (from the key or `RemoteId`);
  - ✕ to remove the mod.
- **Dragging:** the row being dragged over shows a blue line on the side where the mod will land (below when moving down, above when moving up). Dropping calls `ModListEditor.Move`.
- **Removed:** the ↑/↓ buttons and the left panel.
- **Icons:** after each library refresh, the page loads icons in the background (up to 3 at a time), mods in the current list first. It re-renders every 12 icons and when done. Leaving the page cancels this.

### Add mods picker (`Components/AddModsDialog.razor`)
- **Shown:** a modal over the page listing installed mods whose key isn't in the list, sorted by name.
- **Contents:**
  - a search box (by name) and a source filter (All, Workshop, Local);
  - one row per mod: checkbox, 32 px icon, name and badge;
  - at most 300 rows shown, with "search to narrow it down" when there are more.
- **Add N mods:** returns the ticked mods in name order. The page appends them through `AddRange` and reports "Added N mods."
- **Closing:** Esc, ✕, or a click on the backdrop.
- **Focus:** the dialog takes focus when it opens.

## Errors
- Thumbnails that are missing, unreadable or can't be decoded simply show the placeholder.
- Icon work never throws to the UI.
- List editing, saving and applying keep their current error handling.

## Testing
- **ModListEditor:**
  - MoveTo: positions, clamping and returned index;
  - Move: up, down and the same index;
  - AddRange: duplicates (case-insensitive), order and count.
- **ModThumbnail:**
  - the `picture` field wins;
  - `thumbnail.png` fallback, case-insensitive (`Thumbnail.PNG`);
  - `.jpg`;
  - an unsafe `picture` (`../x.png`) is ignored;
  - a zip root entry;
  - a thumbnail beside the zip;
  - nothing found gives null;
  - an empty file is ignored.
- **ModIconCache** (with a fake shrinker):
  - returns a data URI;
  - a second instance reads the disk cache without shrinking again;
  - a changed file stamp makes a new cache entry;
  - a null shrink gives null;
  - TryPeek is null before and set after GetAsync.
- **UI:** checked by hand. That covers drag, the number box, the Workshop link, the picker, and the icons appearing.

## Out of scope
Multi-select drag, sorting the list by name, editing descriptors, Workshop search or subscribing from the picker (sub-project 10 covers installing), and pruning the icon cache.

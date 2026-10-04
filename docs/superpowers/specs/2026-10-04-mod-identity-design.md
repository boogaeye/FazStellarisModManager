# Mod Identity Matching — Design (Sub-project 9)

## Goal
When comparing two machines, the diff (Session tab) and "Match host" decide whether two installed mods are the **same mod** from their descriptor and files, not from where they are installed. A Workshop item (`mod/ugc_<id>.mod`) and a local copy of it (for example an Irony Mod Manager collection export) are the same mod. Equal files mean "Ok", different files mean "Content differs". A note says when one side uses a local copy of a Workshop mod.

Sub-project 10 (real Workshop install and update with a pop-up) builds on this. Sub-project 11 (drag-and-drop priority and Workshop links) follows.

## Facts (user's machine)
- **Irony exports:** the user's list uses local copies, `mod/8cde_<hash>.mod`, with names like `(Fazverse 4.4 with Flamer) DarkSpace` and the original `remote_file_id`, e.g. 2719075597.
- **Mods without a Workshop id:** a few local mods have none, e.g. `(Fazverse 4.4 with Flamer) Ethics Fix`.
- **Today's keys:** `ModKeys.For` gives `ugc:<id>` only for `ugc_<id>.mod` files and `local:<file>` otherwise. It keeps local copies with a `remote_file_id` local on purpose, and `ModDiffer`/`MatchPlan` pair mods only by this key, so a local copy shows as Missing on one side and Extra on the other.
- **Data already available:** snapshots carry `Name`, `RemoteId` (from the descriptor's `remote_file_id`) and file hashes. `InstalledMod` and `ModListEntry` carry `Name` and `RemoteId`.

## Decisions

### Keep keys; add a matching layer
Keys stay as they are, and so do saved lists, `dlc_load.json` and the session protocol. Peers on older app versions keep working. Identity is used only when pairing.

### Identity (`ModIdentity`, `ModMatcher` in `Core/Diff`)
- **Workshop id:** the id from a `ugc:<id>` key, otherwise the descriptor's `remote_file_id` when it is a positive integer, otherwise none.
- **Name:**
  1. lower-cased, invariant;
  2. one leading parenthesised prefix removed, e.g. `(Fazverse 4.4 with Flamer) `;
  3. runs of whitespace collapsed to one space;
  4. trimmed.
- **Files fingerprint:** SHA-1 over the sorted lines `path|md5` (paths lower-cased, `/` separators). There is none when the file list is empty.
- **Pairing:** `Pair(targets, mine)` applies these rules in order:

  | Rule | Pairs by | Applies to |
  |---|---|---|
  | 1. Key | same key (case-insensitive) | all mods |
  | 2. Workshop id | same Workshop id | all mods |
  | 3. Name | same normalised name, non-empty | only when **both** sides have no Workshop id |
  | 4. Files | same fingerprint | all mods |

  Each mod takes part in at most one pair. Each rule considers only mods still unpaired. Within a rule, target mods are taken in their given order, and each one pairs with the first unpaired mine mod with the same value (in mine's order).
- **Result:** `(target, mine, MatchKind)` with `MatchKind` = Key | WorkshopId | Name | Files, ordered by target position.

### Diff (`ModDiffer`)
- **Pairing:**
  - Mods use all four rules.
  - DLCs use only rule 1 (key), as today.
  - Duplicate keys within one snapshot still keep the first occurrence.
- **Paired mods:** compared file by file as before (Ok or ContentMismatch), with my load order.
- **Load order:** the out-of-order check runs over the pairs. A target mod is ranked by its partner's position in my load order. A minimal set of target mods is flagged, using the longest increasing subsequence as today.
- **Unpaired mods:** target mods are Missing; mine are Extra.
- **New `UnitDiff` fields** (appended with defaults, so existing call sites still compile):
  - `MineKey`: the key of my paired mod, or of the Extra mod; null for Missing.
  - `Match`: the MatchKind; Key for Missing and Extra.
  - `LocalCopyOfWorkshop`: true when the pair has the same Workshop id but exactly one side's key is `ugc:`.
  - `MineName`: the paired mod's name.

### Match host (`MatchPlan`)
- **Which of my mods to use for each host mod,** in host order:
  1. its partner from the diff, when the diff paired it (looked up in my library by `MineKey`);
  2. otherwise a library pairing between the host list entries and my installed mods, using rules 1–3 (no files are known for the library);
  3. otherwise none.
- **Applying:** a host mod with a partner is applied using **my** descriptor (key, name, rel and remote id from my installed mod).
- **Without a partner:**
  - an entry with a Workshop id (from its key or `RemoteId`) goes to `NeedsWorkshopInstall`, even when the host itself uses a local copy;
  - any other entry goes to `NeedsManualInstall`.
- **Content mismatches:** these go to `NeedsWorkshopUpdate` when **my** mod is a `ugc:` key (`MineKey`), otherwise to `DiffersLocally`.
- **`ModMatcher.WorkshopIdOf(key, remoteId)`:** the Session page uses it to show the Workshop id for install entries.

### UI
- **DiffView:** a paired row whose `Match` is not Key gets a muted tag: "matched by Workshop id with `<MineKey>`", "matched by name with …" or "matched by identical files with …". When `LocalCopyOfWorkshop` is set it adds a warning-coloured tag: "local copy of Workshop `<id>`".
- **Mods page library:** a local mod with a Workshop id gets the badge "local copy of Workshop `<id>`".

## Errors
Pairing is pure and never throws. A missing name or empty file list just leaves rules 3 and 4 unused for that mod.

## Testing
- **ModMatcher:**
  - each rule;
  - the rule order (key beats Workshop id beats name beats files);
  - a mod used only once (two of mine with the same Workshop id pair once, and the second stays Extra);
  - the name rule skipped when either side has a Workshop id;
  - the prefix and whitespace normalisation;
  - the fingerprint is order-independent and null when empty.
- **ModDiffer:**
  - a local copy with `remote_file_id` pairs with `ugc:` (Ok when the files are equal), setting LocalCopyOfWorkshop and MineKey;
  - a renamed local copy with identical files pairs by Files;
  - a prefixed-name pair with different files gives ContentMismatch;
  - order is checked across the pairs;
  - DLCs are not paired by name;
  - existing tests still pass, with adjustments only where the new rules legitimately pair test units, which must be reported.
- **MatchPlan:**
  - a host `ugc:` entry applied with my local copy's descriptor;
  - a host local copy with a Workshop id that I don't have is offered for Workshop install;
  - a host prefixed-name local mod matched by name;
  - a ContentMismatch on my local copy goes to DiffersLocally, and on my `ugc:` to NeedsWorkshopUpdate.
- **Real data:** compare the user's current snapshot with a synthetic host snapshot that uses `ugc:` keys for the same Workshop ids. All of them should pair, by Workshop id.

## Out of scope
- Installing or updating from the Workshop (sub-project 10).
- Changing stored keys or the protocol.

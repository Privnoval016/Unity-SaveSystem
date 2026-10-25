# Changelog

## 0.5.1 (2026-10-25)

- `SlotId.TryGetNumber`: the number of a numbered manual slot or autosave; false for a named slot (so a
  quicksave named `quicksave-7` is never read as slot 7).

## 0.5.0 (2026-10-25)

From an audit and a five-viewpoint review (correctness, crash safety and concurrency, API, tests, an adversary):
every finding fixed, each with tests in `Tests/Core/Failsafes`.

- **Breaking changes** (migrating below):
  - `ISaveStorage` gains `ReadPending`, `CommitPending` and `Repair`; `Delete` removes the entry last; `List`
    includes an entry with only a pending write.
  - `ReadResult` takes a `ReadSource` instead of a used-backup flag (`UsedBackup` still reads).
  - New enum values a `switch` must handle: `LoadStatus.Busy`, `LoadStatus.Unreadable`, `SaveStatus.TooNew`,
    `SaveStatus.Busy`, `ReadStatus.TooNew`, `ReadStatus.Unreadable`, `DecodeStatus.TooNew`,
    `SlotHeaderState.TooNew`.
  - Asset references that can't be resolved fail the save or load by default (`UnresolvedAssets.Fail`), a destroyed
    asset included; pass `UnresolvedAssets.LoadAsNull` to `UnitySave.CreateSerializer` for the old behaviour.
  - A failed restore is put back (below), where it used to leave the other sections restored.
  - Section JSON is written in ordinal property order, so files written before differ in layout (they load the same),
    and a dictionary read back enumerates in key order.
  - A file in a newer build's file format reads as `TooNew`, not damaged.
- **A load never leaves a half-loaded game.** A section that throws while applying or settling puts every section
  back as it was (`LoadResult.RolledBack`, `LeftStateUnchanged`); a new game the same. Sections that can't be put back
  are saved with their last good data (`SaveService.PinnedKeys`) and the result says `NeedsReset`.
- **`ISettlesAfterRestore`**: state derived from other sections settles once every section is back. A settle throw
  as a section registers leaves it unregistered with its data held.
- **Nothing saves or starts a new game during a load**: saves, autosaves, loads and `SaveSlots.NewGame()` asked for
  while a load runs return `Busy` (`SaveSlots.Loading`, `SettingsStore.Loading`).
- **A newer build's save is never lost to an older build**: a newer file format, document format or section version
  (`SaveService.IsFromNewerBuild`, safe on the worker thread), in the save, its backup or a pending write, reads as
  `TooNew`, is never replaced by anything older, and is never written over; autosave rotates around such a slot, and
  around one that can't be read just now.
- **Writes**: `File.Replace` instead of deleting the backup first, with a fallback where it isn't supported; a blocked
  move or read is retried (I/O errors three times, access errors once); a failed write removes its temp file, except
  a whole one after a swap failed halfway; a whole temp file left by a crash is read and moved (never rewritten) into
  place; repairing from the backup keeps the backup and only repairs real damage (never another key's file); a
  backup is good only when it parses.
- **Reads**: a file that is there but can't be read (the save, its backup or a pending write) is `Unreadable`,
  never replaced by anything older.
- **Determinism**: LF line endings everywhere; properties in ordinal order at every depth (dictionaries and scene
  entities no longer leak their order); a token another JSON tree holds is copied before it is sorted.
- **Slots**: a slot write never throws (failures are results, unexpected ones logged); the header's time is the
  capture time; `ListAsync(checkSaves: true)` reads each save (`SlotSaveState`), rewrites stale, missing and damaged
  headers; autosave ranks a slot by the higher of its header's and its save's sequence.
- **A section that fails to capture as it unregisters** keeps its last saved state.
- **`Unregister(section, keepState: false)`** forgets a section's state (an area left whose world resets).
- **Gzip** unpacks at most 256 MB by default.
- **`SaveStorageContractTests`**: an abstract fixture a storage backend subclasses to prove it keeps the contract.
- Menus moved under one Privnoval heading: *Tools > Privnoval > Save System* and
  *Add Component > Privnoval > Save System*.

**Migrating from 0.4:**
- A custom `ISaveStorage`: add `ReadPending` (null if your puts are atomic), `CommitPending` (nothing, likewise) and
  `Repair` (replace the entry, keep the backup); delete the entry last. Subclass `SaveStorageContractTests`.
- Code that built `ReadResult` passes a `ReadSource`.
- Handle the new statuses; treat `LoadResult.NeedsReset` as fatal.
- Data that held asset references which may disappear: hold their ids as text, or opt into `LoadAsNull`.

## 0.4.0 (2026-10-19)

- **Asset ids moved to Unity-Extensions** (`Extensions.Ids`), so every module can use them. The save system now
  references Extensions.
  - Renamed: `ISaveableAsset` is now `IIdentifiedAsset`, and `SaveableAssets` is now `AssetIds`.
  - The id menus are now under *Tools > Asset Ids*.
  - What stays here: the JSON converters (`AssetIdJsonConverter` is registered by `UnitySave.CreateSerializer`),
    world objects, the scene id audit and its own build check (`SceneIdBuildCheck`), and the browser.
  - Nothing changes on disk.
- **Tests:** 349 here (39 moved to Extensions).

## 0.3.0 (2026-10-19)

From the second review, and a pass on scale.

- **Breaking changes:**
  - Sections restore in registration order (`RestoreOrder` is gone).
  - The worker is a required argument of `SaveFileOptions` and `SlotOptions`.
  - `IAssetResolver.TryFind` returns a `UnityEngine.Object`.
  - `LoadResult.FromBackup` is now `WithBackup`.
  - `SlotHeader` field names live in `SlotHeaderFields`.
  - `SaveDocumentQueue` and `AssetCatalog.SetAssets` are internal.
- **Safer:**
  - Upgrades must cover every older version, checked when a section registers.
  - A document upgrade that leaves the save invalid fails the load (`UpgradeFailed`).
  - A section unregistered between prepare and apply is not restored.
  - A section registered between prepare and apply with no data gets its default.
  - A saved but unreadable file (another key or transform) is never treated as damaged.
  - A failed header write removes the stale header.
  - The swap rollback never hides the original error.
  - The id fixer saves each asset as it writes it.
- **Scale:**
  - An incremental id index: no project scan per import.
  - Ids are assigned when a type becomes saveable.
  - Catalogs are merged rather than rebuilt.
  - The slot sequence is cached.
  - The backup check reads the frame and checksum only.
  - The entity id check is linear.
  - Section lookup is by dictionary.
  - Entity captures skip a copy.
  - Engine vectors are written as floats.
- **Added:**
  - The scene id audit, also run before builds.
  - A *Fix Asset Ids* menu.
  - An error for duplicate component keys on an entity.
  - `UnitySaveLog.Instance`.
  - A `Log` option for slots.
  - The scale test.
- **Tests:** 388.

## 0.2.0 (2026-10-19)

Fixes and additions from the first code review.

- **Data safety:**
  - A section whose held data fails to restore when it registers stays unregistered, and its data stays held.
  - A section registered between prepare and apply is restored.
  - Saving over a damaged file keeps the good backup.
  - Data nested too deep to read back is refused at save.
  - A failed file swap puts the old file back, and an entry left with only its backup is still listed.
  - An entity destroyed without `Despawn` keeps its state.
  - A component that throws while capturing keeps its last state.
- **Fixes:**
  - `AssetId` now saves as JSON.
  - Both converters skip unexpected objects and lists.
  - Autosave rotation reads the sequence from the save when its header is lost.
  - A save copied into another slot gets that slot's header.
  - The save browser keeps date-like strings and can be given transforms.
  - Entry names exclude Windows device names.
- **Added:**
  - `DocumentUpgrade`, for whole-save changes: rename, split, merge, retire.
  - AES transform ids, for key rotation.
  - A header format number; headers are read without converters.
  - `SaveDocumentQueue`, shared by slots and settings.
  - `SlotEntries`.
  - `SaveFileCodec.ReadTransformIds`.
  - `SceneSectionOptions`.
- **Changed:**
  - `LoadResult` is immutable.
  - `SaveService.Capture` takes no meta (slots set it).
  - `MemorySaveStorage.Overwrite` is internal.
- **Tests:** 370.

## 0.1.0 (2026-10-19)

First version.

- **Core** (`SaveSystem.Core`, no engine references):
  - sections with keys, versions and JSON upgrades;
  - all-or-nothing loads, with prepare and apply as two steps;
  - held data for systems that aren't registered;
  - a file format with a SHA-256 checksum, atomic writes and a backup;
  - gzip and AES transforms;
  - file and memory storage;
  - a background worker that runs jobs in order per queue;
  - slots with sidecar headers and rotating autosaves;
  - a settings store.
- **Unity layer** (`SaveSystem`):
  - asset ids and the asset catalog;
  - JSON for saveable asset references and engine value types;
  - saveable scenes, entities and components;
  - Unity defaults (folders, a worker per platform, flush on quit).
- **Editor** (`SaveSystem.Editor`):
  - an id postprocessor and planner;
  - the catalog builder;
  - the id audit and a build check;
  - the save browser window.
- **Tests:** 294 EditMode tests and frozen save fixtures.

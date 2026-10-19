# Changelog

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

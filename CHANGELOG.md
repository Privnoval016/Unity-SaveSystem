# Changelog

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

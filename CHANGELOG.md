# Changelog

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

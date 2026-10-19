# Unity-SaveSystem

A save system for Unity games that can be dropped into any project. Each system saves its own section of the
save. Saves are versioned JSON, written safely in the background, with slots, autosaves, settings, references to
assets by stable id, and saved world objects.

- **Requires:** Unity 6, and Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`) as a direct dependency.
- **Namespace:** `SaveSystem` (editor tools: `SaveSystem.EditorTools`).

## Assemblies

| Assembly | Folder | What it holds |
|---|---|---|
| `SaveSystem.Core` | `Core/` | Everything that isn't Unity: sections, the service, upgrades, the file format, storage, the worker, slots, settings. It has **no engine references**, so code that runs on a background thread can't touch Unity by mistake |
| `SaveSystem` | `Unity/` | Asset ids and the catalog, JSON for engine types and asset references, world objects, Unity defaults |
| `SaveSystem.Editor` | `Editor/` | The id postprocessor, catalog builder, audit and build check (editor only) |
| `SaveSystem.Core.Tests`, `SaveSystem.Tests`, `SaveSystem.Editor.Tests` | `Tests/` | EditMode tests, compiled only when tests are included |

None of these assemblies is auto-referenced. A game assembly lists `SaveSystem.Core`, plus `SaveSystem` if it
uses ids, the catalog or world objects.

## How a system saves: sections

A section is one system's part of the save. It has:

- a **key**: lower-case letters, digits and `.` `_` `-` `/`, at most 64 characters, and never changed;
- a **version**, starting at 1;
- a **capture**, which turns live state into a plain data class;
- a **restore**, which turns that data class back into live state.

```csharp
public sealed class InventoryData
{
    public List<ItemDefinition> Items = new List<ItemDefinition>();
    public int Coins;
}

public sealed class InventorySection : SaveSection<InventoryData>
{
    private readonly Inventory _inventory;

    public InventorySection(Inventory inventory) => _inventory = inventory;

    public override string Key => "inventory";

    protected override InventoryData Capture() => new InventoryData { Items = new List<ItemDefinition>(_inventory.Items), Coins = _inventory.Coins };

    protected override void Restore(InventoryData data) => _inventory.Set(data.Items, data.Coins);
}
```

- **The data class is the schema on disk.** Rename or remove a field only with an upgrade (below).
- **`Capture` must return a snapshot** that shares no lists with live state.
- **`CreateDefault`** gives a new game's state. A save with no data for the section restores this, which covers
  a new game and a system added after the save was made.
- **`RestoreOrder`** orders sections that depend on each other: lower restores first.

**Rules for data classes:**

- **Use fields or settable properties for collections.** A load replaces a list it can set. A get-only list that
  starts with items would be appended to.
- **Don't key dictionaries by assets.** A dictionary's keys are written as text, not through the asset converter.
  Use a list of entries, each holding the asset and its value.
- **Numbers go through `double`.** `decimal` keeps about 15 significant digits, and `-0` loads as `0`.
- **On platforms that strip code (IL2CPP)**, keep data classes from being stripped with `[Preserve]` or a
  `link.xml` entry.

### Registering

```csharp
var service = new SaveService(UnitySave.CreateSerializer(assetCatalog), new UnitySaveLog());
service.Register(new InventorySection(inventory));
```

- **A duplicate key throws, as does an invalid key, version or upgrade.** That is a bug, so it fails loudly.
- **When a section registers and data is held for it, it is restored at once.** If that data can't be used, or the
  restore throws, the section is not registered, the data stays held, and the result says why.
- **Unregistering keeps the section's current state held.** It goes into the next save, and the section is
  restored from it when it registers again. A scene's world state uses this as areas load and unload.
- **Data for a key no system has registered is held unchanged and written back**, whether the system was
  removed or the save is from a newer build. Nothing is ever silently dropped.

## Loading is all or nothing

A load first upgrades and reads **every** section's data, and only then applies any of it. If any section's data
can't be used, nothing changes and the result says why:

- a missing upgrade step;
- an upgrade that throws;
- data that doesn't fit the class;
- data from a newer version than the code.

The only partial outcome is a section that throws while applying its data. It is reported as `RestoreFailed`
and the other sections are still restored.

`service.Prepare(document)` and `service.Apply(prepared)` expose the two steps for code that needs them. A section
registered between the two is restored by `Apply` too.

Every load returns a `LoadResult`:

| Field | What it holds |
|---|---|
| `Status` | `Loaded`, `Missing`, `Damaged`, `TooNew`, `SectionFailed` or `RestoreFailed` |
| `UsedBackup` | True when the save was damaged and its backup was loaded instead |
| `Problem` | What went wrong |
| `Sections` | What happened to each section: restored, upgraded, defaulted, held or failed |

Expected failures come back as results, never as exceptions.

## Versions and upgrades

When a data class changes shape, raise the section's `Version` and add one `SectionUpgrade` per older version.
An upgrade works on the **raw JSON**, so old data classes never need to be kept:

```csharp
public override int Version => 2;
public override IReadOnlyList<SectionUpgrade> Upgrades => Steps;

private static readonly SectionUpgrade[] Steps =
{
    new SectionUpgrade(1, data =>
    {
        data["Coins"] = data["Gold"];
        ((JObject)data).Remove("Gold");
        return data;
    }),
};
```

- **Steps run in order.** Data at version 1 runs step 1, then step 2, and so on.
- **Each step gets a copy of the data**, so changing it in place is safe.
- **Keep a real save from each version as a test fixture**, and test that it loads to the expected state. See
  `Tests/Core/Fixtures`. Fixtures are frozen and never regenerated.

### Changes across the whole save

Renaming a section's key, splitting a section in two, merging two, or dropping a retired one is a
`DocumentUpgrade`. It runs once per save, before any section reads its data:

```csharp
service.AddDocumentUpgrade(new DocumentUpgrade("party-to-roster", document =>
{
    if (!document.Sections.TryGetValue("party", out var record)) return;
    document.Sections.Remove("party");
    document.Sections["roster"] = record;
}));
```

- **Each save records the ids of the upgrades it has had**, so an upgrade never runs twice on one save. A new
  save records every upgrade, since its data is already current.
- **Ids are permanent.** Never change or reuse one.
- **Upgrades run on a copy, in the order added.** One that throws fails the load and changes nothing.

## Slots, autosaves and settings

```csharp
var slots = new SaveSlots<SlotInfo>(service, new FileSaveStorage(UnitySave.SavesFolder), CaptureSlotInfo,
    new SlotOptions { Worker = worker, AutosaveCount = 3 });

await slots.SaveAsync(SlotId.Manual(1));
await slots.AutosaveAsync();                 // the empty or oldest of auto-01..auto-03
var list = await slots.ListAsync();          // headers only: never reads a save
var result = await slots.LoadAsync(SlotId.Manual(1));
await slots.DeleteAsync(SlotId.Manual(1));
```

- **Saving captures on the calling thread at once.** The file is then written in the background, so an
  autosave causes no hitch. Later changes to live state never leak into a save already captured.
- **Loading reads and checks the file in the background**, then restores on the thread that awaited it. Call it
  from the main thread.
- **One job runs at a time.** A load waits for a save in progress, and the second of two saves to a slot wins.
- **Each slot has a small header file beside it** (`<slot>.head`). It holds:
  - a format number;
  - the slot's name;
  - a sequence number;
  - the time;
  - the game's own `TInfo` fields (chapter, place, play time).

  The save screen lists slots from these without reading saves. A copy of the header also lives inside the save,
  so a lost or stale header is rewritten when the slot loads, or by `RepairHeaderAsync`.
- **`TInfo` is plain data** (text, numbers, ids as text). Headers are read in the background without the asset
  and engine converters.
- **Autosaves rotate by sequence number, not by the clock**, so a changed system clock can't make the newest
  autosave look oldest. When a header is lost, the sequence comes from the copy inside the save.
- **Share one worker** between the slots and the settings, so one flush waits for both.
- **Settings are separate:** a `SettingsStore` with its own `SaveService` and its own folder
  (`UnitySave.SettingsFolder`), so deleting every slot never touches them.
- **On quit**, `UnitySave.FlushOnQuit(worker, timeout)` waits for writes still in progress.
- **`UnitySave.CreateWorker()`** returns a background worker, or an immediate one on WebGL, which has no threads.

## The file format, and why each save has a checksum

Each file is one header line, a newline, then the payload:

```text
{"format":1,"length":492,"sha256":"de04…","transforms":[]}
{
  "format": 1,
  "meta": { …slot header… },
  "sections": { "inventory": { "version": 2, "data": { … } } }
}
```

**Saving is atomic.** The file is written to `<name>.tmp` and flushed to disk. The current file then becomes
`<name>.bak`, and the new file takes its place. A crash at any moment leaves either the old save or its backup.

- **If the swap fails**, the old file is put back.
- **A save left with only its backup** is still listed and loads from the backup.
- **Saving over a damaged file** keeps the good backup rather than the damage.
- **Data nested deeper than `SaveSerializer.MaxDepth` (128) is refused at save**, so nothing unreadable is ever
  written.
- **Entry names are lower case**, and never a name Windows reserves (`con`, `nul`, `com1`, ...).

**The checksum is SHA-256 over the payload.** Atomic writes only protect against our own crash mid-save. They
don't protect against what happens to a file afterwards:

- a full disk;
- a cloud-sync conflict;
- a platform cutting a file short;
- a hand edit.

Damage like that can still parse as valid JSON with data missing and quietly restore the wrong state. With the
checksum, the load sees the damage and falls back to the backup, and `LoadResult.UsedBackup` says so. It costs
under a millisecond per save.

**Transforms are optional and off by default**, so saves stay readable while developing:

- `GzipTransform` compresses;
- `AesTransform` encrypts with a key the game supplies.

The key ships with the game, so encryption stops casual editing, not a determined player.

- **The header records which transforms ran.**
- **After switching a transform off,** pass it as `alsoReadable` so old saves still load.
- **To change the AES key,** give the new key a new id (`new AesTransform(newKey, "aes-2")`) and keep the old one
  readable.

**The JSON rules are the same on every machine:**

- numbers are culture-invariant;
- dates are UTC ISO 8601;
- NaN and infinities are written as strings;
- strings that look like dates stay strings;
- lists are replaced on load, never appended to;
- type names in a save are ignored, so a save file can never choose a type to construct.

**Storage sits behind `ISaveStorage`.** `FileSaveStorage` keeps files in one folder; `MemorySaveStorage` is for
tests. A console's or a cloud's storage would be another implementation.

## Asset ids: saving references to assets

A save can't point at an asset by name or path, because those change. An asset a save may point at implements
`ISaveableAsset` and holds one `AssetId` field:

```csharp
public sealed class ItemDefinition : ScriptableObject, ISaveableAsset
{
    [Tooltip("Filled in by the editor.")]
    [SerializeField] private AssetId assetId;

    public AssetId AssetId => assetId;
}
```

**The editor fills the id in**, with no typing:

- A new asset gets its file's GUID.
- A **duplicated** asset gets an id of its own. The original, the one whose GUID it is, keeps its id.
- An id once stored **never changes**, even if the asset's `.meta` file is regenerated, so saves keep working.

**Saving a reference needs no code.** A data class just holds the asset: `public ItemDefinition Item;`. The
serializer from `UnitySave.CreateSerializer(catalog)` writes its id and finds the asset again on load. An id
with no asset, or with an asset of the wrong type, loads as null with one warning. An `AssetId` field saves as its
32 hex digits.

**Builds find assets through an `AssetCatalog`.** Create one with *Create > Save System > Asset Catalog* and pass
it to `CreateSerializer`. The editor keeps it filled:

- on every import;
- before every build;
- from *Tools > Save System > Rebuild Asset Catalog*.

The catalog references every saveable asset, so they load with it.

**The audit checks every saveable asset** (*Tools > Save System > Audit Asset Ids*):

- it has exactly one id field;
- it has an id;
- the id is unique;
- every catalog lists it.

A build fails if any id is still missing or shared after the automatic fix.

## World objects

For things in a scene that remember their state, such as chests, doors, levers and pickups:

1. **Add a `SaveableScene`** to each scene that saves. It takes its id from the scene's GUID. A scene copied from
   another needs *New Scene Id* from its context menu.
2. **Add a `SaveableEntity`** to each object that saves. The editor gives each one an id that is unique in the
   scene, and a duplicated object gets a new id.
3. **Write its saved components** as `SaveableComponent<TData>`, with a key, plus a version and upgrades if they
   change. With no saved data, a component keeps the state the scene gave it.
4. **Remove an object for good with `entity.Despawn()`.** The scene's save remembers it is gone, and it is
   removed again whenever the scene loads.
5. **Register the scene's section when the scene loads, and unregister it when the scene unloads.** Its state is
   held in between, so leaving an area and coming back keeps everything:

```csharp
var section = SaveableScene.FindIn(scene).CreateSection(serializer);
service.Register(section);   // restores the scene from the loaded save, if it has data
...
service.Unregister(section); // holds the scene's state until it loads again
```

- **Each entity is its own small save.** Its components can upgrade, a removed component's data is kept, and one
  entity whose data can't be read fails the whole scene's restore rather than half of it.
- **An entity destroyed without `Despawn`** has its state kept as it was. Unloading the scene is one example.
- **A component that throws while capturing keeps its last saved state**, with a warning, and the save goes on.
- **Data for entities no longer in the scene is kept.**
- **Reload a scene before loading a save into it.** A scene section restores saved state, but it can't undo what
  happened in the scene since it loaded, such as a chest opened or a pickup taken.
- **Only objects placed in the scene are saved.** Objects spawned at runtime belong in a system's own section.
- **`CreateSection(serializer, options)`** takes a `SceneSectionOptions` with the restore order and the log.

## Tools

*Tools > Save System* in the editor:

| Tool | What it does |
|---|---|
| **Save Browser** | Lists the saves and settings in a folder. Shows each section's version and data. Edits a section and writes the save back with a fresh checksum (the old file becomes the backup). Deletes a save and its header. Opens the folder |
| **Rebuild Asset Catalog** | Fills every catalog now |
| **Audit Asset Ids** | Logs every id problem |

The browser reads plain and gzip saves. Code that opens `SaveBrowserModel` itself can pass the AES transforms the
game uses, to read encrypted saves as well. Deleting asks inside the window, never in a dialog.

## Tests

The tests run in EditMode under the three test assemblies. Together they cover:

- registration and key rules;
- round trips with awkward values;
- all-or-nothing loads and upgrades;
- cultures (German, Turkish, French, Arabic);
- dates, NaN, and type names in data;
- damaged, cut-short, interrupted and garbled files;
- gzip and AES, including the wrong key;
- crashes mid-write;
- concurrency (50 interleaved saves, a load waiting for a save, flushing on quit);
- slots, rotation and header repair;
- settings;
- frozen fixtures;
- asset ids, the planner and the catalog on real assets;
- world objects.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>How a scene section restores and where it reports problems.</summary> */
    public sealed class SceneSectionOptions
    {
        #region Properties

        /** <summary>When the scene restores relative to other sections.</summary> */
        public int RestoreOrder { get; set; }

        /** <summary>Where problems go.</summary> */
        public ISaveLog Log { get; set; } = new UnitySaveLog();
    

        #endregion
    }

    /**
     * <summary>One scene's saved world: each entity's components, and which entities were despawned.</summary>
     * <remarks>
     * Each entity is its own small save, so components have versions and upgrades, and a removed component's data is
     * kept. One entity whose data can't be read fails the whole scene's restore. Data for entities not in the scene is
     * kept. With no saved data, the scene keeps its authored state, so reload a scene before loading a save into it.
     * </remarks>
     */
    public sealed class SceneSaveSection : ISaveSection
    {
        #region Private Systems

        private readonly ISaveLog _log;
        private readonly SaveSerializer _serializer;

        #endregion

        #region Private Fields

        private const string EntitiesProperty = "entities";
        private const string DespawnedProperty = "despawned";

        /** <summary>Keyed by entity id, resolving to the live entity and its components' service.</summary> */
        private readonly Dictionary<string, EntityState> _live = new Dictionary<string, EntityState>(StringComparer.Ordinal);

        /** <summary>Keyed by entity id, resolving to saved data for entities that are not in the scene.</summary> */
        private Dictionary<string, JObject> _absent = new Dictionary<string, JObject>(StringComparer.Ordinal);

        private HashSet<string> _despawned = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public string Key { get; }

        /** <inheritdoc /> */
        public int Version => 1;

        /** <inheritdoc /> */
        public int RestoreOrder { get; }

        /** <inheritdoc /> */
        public IReadOnlyList<SectionUpgrade> Upgrades => SectionUpgrade.None;

        /** <summary>The ids of entities despawned in this scene.</summary> */
        public IReadOnlyCollection<string> Despawned => _despawned;

        #endregion

        #region Public API

        /**
         * <summary>Makes a scene's section.</summary>
         * <param name="key">The section's key, such as <c>world/</c> and the scene's id.</param>
         * <param name="entities">The scene's entities. Entities with no id, or a repeated id, are skipped with a warning.</param>
         * <param name="serializer">Components are captured and read with it: the same one as the game's service.</param>
         * <param name="options">Restore order and log.</param>
         */
        public SceneSaveSection(string key, IEnumerable<SaveableEntity> entities, SaveSerializer serializer, SceneSectionOptions options = null)
        {
            options = options ?? new SceneSectionOptions();
            Key = key;
            RestoreOrder = options.RestoreOrder;
            _log = options.Log ?? SilentSaveLog.Instance;
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            foreach (var entity in entities)
                Track(entity);
        }

        /** <inheritdoc /> <remarks>Uses the serializer given at construction.</remarks> */
        public JToken Capture(JsonSerializer serializer)
        {
            var entities = new JObject();
            foreach (var pair in _absent)
                entities[pair.Key] = pair.Value.DeepClone();
            foreach (var pair in _live)
            {
                var json = CaptureEntity(pair.Value);
                if (json != null) entities[pair.Key] = json;
            }

            var despawned = new List<string>(_despawned);
            despawned.Sort(StringComparer.Ordinal);
            return new JObject { [EntitiesProperty] = entities, [DespawnedProperty] = new JArray(despawned) };
        }

        /** <inheritdoc /> <remarks>Uses the serializer given at construction.</remarks> */
        public IPreparedRestore Prepare(JToken data, JsonSerializer serializer)
        {
            if (data == null || data.Type == JTokenType.Null) return PrepareDefault();
            if (!(data[EntitiesProperty] is JObject entities)) throw new FormatException("'entities' must be an object.");

            var scene = new PreparedScene(this);
            foreach (var id in data[DespawnedProperty]?.Values<string>() ?? Array.Empty<string>())
                if (id != null) scene.Despawned.Add(id);

            foreach (var property in entities.Properties())
            {
                if (!(property.Value is JObject entityJson)) throw new FormatException($"Entity '{property.Name}' must be an object.");
                if (!_live.TryGetValue(property.Name, out var state) || !state.IsAlive)
                {
                    scene.Absent[property.Name] = entityJson;
                    continue;
                }
                var load = state.Service.Prepare(SaveDocument.FromJson(entityJson));
                if (!load.CanApply) throw new FormatException($"Entity '{property.Name}': {load.Failure}");
                scene.Loads.Add(new EntityLoad(state, load, entityJson));
            }
            return scene;
        }

        /** <inheritdoc /> */
        public IPreparedRestore PrepareDefault() => new PreparedScene(this);

        #endregion

        #region Event Handlers

        private void HandleDespawned(SaveableEntity entity)
        {
            entity.OnDespawned -= HandleDespawned;
            entity.OnDestroyed -= HandleDestroyed;
            _despawned.Add(entity.Id);
            _live.Remove(entity.Id);
        }

        private void HandleDestroyed(SaveableEntity entity)
        {
            entity.OnDespawned -= HandleDespawned;
            entity.OnDestroyed -= HandleDestroyed;
            if (!_live.TryGetValue(entity.Id, out var state) || state.Entity != entity) return;
            var json = CaptureEntity(state);
            if (json != null) _absent[entity.Id] = json;
            _live.Remove(entity.Id);
        }

        #endregion

        #region Helpers

        private void Track(SaveableEntity entity)
        {
            if (entity == null) return;
            if (string.IsNullOrEmpty(entity.Id))
            {
                _log.Warning($"Save: {entity.name} has no entity id; not saved.");
                return;
            }
            if (_live.ContainsKey(entity.Id))
            {
                _log.Warning($"Save: {entity.name} repeats entity id {entity.Id}; not saved.");
                return;
            }

            var service = new SaveService(_serializer, _log);
            foreach (var component in entity.GetComponents<ISaveableComponent>())
            {
                try
                {
                    service.Register(component);
                }
                catch (Exception e)
                {
                    _log.Warning($"Save: {entity.name}: {e.Message}");
                }
            }
            _live.Add(entity.Id, new EntityState(entity, service));
            entity.OnDespawned += HandleDespawned;
            entity.OnDestroyed += HandleDestroyed;
        }

        private JObject CaptureEntity(EntityState state)
        {
            if (!state.IsAlive) return state.LastKnown;
            try
            {
                state.LastKnown = state.Service.Capture().ToJson();
            }
            catch (SectionCaptureException e)
            {
                _log.Warning($"Save: entity {state.Id} kept its last saved state: {e.Message}");
            }
            return state.LastKnown;
        }

        private void ApplyScene(PreparedScene scene)
        {
            _absent = scene.Absent;
            _despawned = scene.Despawned;
            string failed = null;
            foreach (var load in scene.Loads)
            {
                if (!load.State.IsAlive) continue;
                var result = load.State.Service.Apply(load.Load);
                load.State.LastKnown = load.Json;
                if (!result.Succeeded) failed = $"entity {load.State.Id}: {result}";
            }
            foreach (var id in new List<string>(_despawned))
                if (_live.TryGetValue(id, out var state) && state.IsAlive) state.Entity.Despawn();
            if (failed != null) throw new InvalidOperationException(failed);
        }

        /** <summary>A live entity, the service its components are saved through, and its last captured state.</summary> */
        private sealed class EntityState
        {
            public SaveableEntity Entity { get; }
            public SaveService Service { get; }
            public string Id { get; }
            public JObject LastKnown { get; set; }
            public bool IsAlive => Entity != null;

            public EntityState(SaveableEntity entity, SaveService service)
            {
                Entity = entity;
                Service = service;
                Id = entity.Id;
            }
        }

        /** <summary>One entity's prepared load and the data it came from.</summary> */
        private readonly struct EntityLoad
        {
            public EntityState State { get; }
            public PreparedLoad Load { get; }
            public JObject Json { get; }

            public EntityLoad(EntityState state, PreparedLoad load, JObject json)
            {
                State = state;
                Load = load;
                Json = json;
            }
        }

        /** <summary>A scene's data, read and waiting to be applied.</summary> */
        private sealed class PreparedScene : IPreparedRestore
        {
            private readonly SceneSaveSection _section;

            public List<EntityLoad> Loads { get; } = new List<EntityLoad>();
            public Dictionary<string, JObject> Absent { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
            public HashSet<string> Despawned { get; } = new HashSet<string>(StringComparer.Ordinal);

            public PreparedScene(SceneSaveSection section) => _section = section;

            public void Apply() => _section.ApplyScene(this);
        }

        #endregion
    }
}

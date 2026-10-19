using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>One scene's saved world: each entity's components, and which entities were despawned.</summary>
     * <remarks>
     * Each entity is its own small save (its components are its sections), so components have versions and
     * upgrades, a removed component's data is kept, and one bad entity fails the whole scene's restore, not half
     * of it. Data for entities not in the scene is kept. With no saved data, the scene keeps its authored state.
     * </remarks>
     */
    public sealed class SceneSaveSection : ISaveSection
    {
        #region Private Fields

        private const string EntitiesProperty = "entities";
        private const string DespawnedProperty = "despawned";
        private static readonly SectionUpgrade[] NoUpgrades = Array.Empty<SectionUpgrade>();

        private readonly ISaveLog _log;

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
        public IReadOnlyList<SectionUpgrade> Upgrades => NoUpgrades;

        /** <summary>The ids of entities despawned in this scene.</summary> */
        public IReadOnlyCollection<string> Despawned => _despawned;

        #endregion

        #region Public API

        /**
         * <param name="key">The section's key, such as <c>world/</c> and the scene's id.</param>
         * <param name="entities">The scene's entities. Entities with no id, or a repeated id, are skipped with a warning.</param>
         * <param name="serializer">The serializer components are captured with: the same one as the game's service.</param>
         * <param name="restoreOrder">When the scene restores relative to other sections.</param>
         * <param name="log">Where problems go.</param>
         */
        public SceneSaveSection(string key, IEnumerable<SaveableEntity> entities, SaveSerializer serializer, int restoreOrder = 0, ISaveLog log = null)
        {
            Key = key;
            RestoreOrder = restoreOrder;
            _log = log ?? new UnitySaveLog();
            foreach (var entity in entities)
                Track(entity, serializer);
        }

        /** <inheritdoc /> */
        public JToken Capture(JsonSerializer serializer)
        {
            var entities = new JObject();
            foreach (var pair in _absent)
                entities[pair.Key] = pair.Value.DeepClone();
            foreach (var pair in _live)
                if (pair.Value.Entity != null) entities[pair.Key] = pair.Value.Service.Capture().ToJson();

            var despawned = new List<string>(_despawned);
            despawned.Sort(StringComparer.Ordinal);
            return new JObject { [EntitiesProperty] = entities, [DespawnedProperty] = new JArray(despawned) };
        }

        /** <inheritdoc /> */
        public IPreparedRestore Prepare(JToken data, JsonSerializer serializer)
        {
            if (data == null || data.Type == JTokenType.Null) return PrepareDefault();
            if (!(data[EntitiesProperty] is JObject entities)) throw new FormatException("'entities' must be an object.");

            var restore = new PreparedScene(this);
            foreach (var id in data[DespawnedProperty]?.Values<string>() ?? Array.Empty<string>())
                if (id != null) restore.Despawned.Add(id);

            foreach (var property in entities.Properties())
            {
                if (!(property.Value is JObject entityJson)) throw new FormatException($"Entity '{property.Name}' must be an object.");
                if (!_live.TryGetValue(property.Name, out var state) || state.Entity == null)
                {
                    restore.Absent[property.Name] = entityJson;
                    continue;
                }
                var load = state.Service.Prepare(SaveDocument.FromJson(entityJson));
                if (!load.CanApply) throw new FormatException($"Entity '{property.Name}': {load.Failure}");
                restore.Loads.Add(new KeyValuePair<EntityState, PreparedLoad>(state, load));
            }
            return restore;
        }

        /** <inheritdoc /> */
        public IPreparedRestore PrepareDefault() => new PreparedScene(this);

        #endregion

        #region Event Handlers

        private void HandleDespawned(SaveableEntity entity)
        {
            entity.OnDespawned -= HandleDespawned;
            _despawned.Add(entity.Id);
            _live.Remove(entity.Id);
        }

        #endregion

        #region Helpers

        private void Track(SaveableEntity entity, SaveSerializer serializer)
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

            var service = new SaveService(serializer, _log);
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
        }

        private sealed class EntityState
        {
            public SaveableEntity Entity { get; }
            public SaveService Service { get; }

            public EntityState(SaveableEntity entity, SaveService service)
            {
                Entity = entity;
                Service = service;
            }
        }

        private sealed class PreparedScene : IPreparedRestore
        {
            private readonly SceneSaveSection _section;

            public List<KeyValuePair<EntityState, PreparedLoad>> Loads { get; } = new List<KeyValuePair<EntityState, PreparedLoad>>();
            public Dictionary<string, JObject> Absent { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
            public HashSet<string> Despawned { get; } = new HashSet<string>(StringComparer.Ordinal);

            public PreparedScene(SceneSaveSection section) => _section = section;

            public void Apply()
            {
                _section._absent = Absent;
                _section._despawned = Despawned;
                string failed = null;
                foreach (var pair in Loads)
                {
                    var result = pair.Key.Service.Apply(pair.Value);
                    if (!result.Succeeded) failed = $"{pair.Key.Entity.name}: {result}";
                }
                foreach (var id in new List<string>(Despawned))
                    if (_section._live.TryGetValue(id, out var state) && state.Entity != null) state.Entity.Despawn();
                if (failed != null) throw new InvalidOperationException(failed);
            }
        }

        #endregion
    }
}

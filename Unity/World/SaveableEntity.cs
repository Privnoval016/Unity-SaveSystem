using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SaveSystem
{
    /**
     * <summary>Gives a scene object a stable id so its saveable components are saved, and so removing it in play
     * (a picked-up item) is remembered.</summary>
     */
    [DisallowMultipleComponent]
    public sealed class SaveableEntity : MonoBehaviour
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor, unique in the scene. Never edit it: saves point at it.")]
        [SerializeField] private string entityId;

        #endregion

        #region Properties

        /** <summary>The entity's id; empty on a prefab, which has no place in a scene yet.</summary> */
        public string Id => entityId ?? string.Empty;

        /** <summary>Raised by <see cref="Despawn" /> before the object is destroyed.</summary> */
        public event Action<SaveableEntity> OnDespawned = delegate { };

        /** <summary>Raised when the object is destroyed any other way, such as its scene unloading, while its components still answer.</summary> */
        public event Action<SaveableEntity> OnDestroyed = delegate { };

        #endregion

        #region MonoBehaviour Callbacks

        private void OnDestroy() => OnDestroyed(this);

#if UNITY_EDITOR
        private void OnValidate()
        {
            ReportDuplicateComponentKeys();
            if (PrefabUtility.IsPartOfPrefabAsset(this) || PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                if (string.IsNullOrEmpty(entityId)) return;
                entityId = string.Empty;
                EditorUtility.SetDirty(this);
                return;
            }
            if (string.IsNullOrEmpty(entityId) || IsSharedInItsScene()) AssignNewId();
        }
#endif

        #endregion

        #region Public API

        /** <summary>Removes the object for good: the scene's save remembers it is gone.</summary> */
        public void Despawn()
        {
            OnDespawned(this);
            OnDestroyed = delegate { };
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /** <summary>Gives the entity a new id. Its saved state is left behind under the old one.</summary> */
        [ContextMenu("New Entity Id")]
        public void AssignNewId()
        {
            entityId = AssetId.New().Value;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        #endregion

        #region Helpers

#if UNITY_EDITOR
        /** <summary>Keyed by scene, resolving to the entities by id seen in that scene since its hierarchy last changed.</summary> */
        private static readonly Dictionary<Scene, Dictionary<string, SaveableEntity>> IdsByScene = new Dictionary<Scene, Dictionary<string, SaveableEntity>>();

        static SaveableEntity() => EditorApplication.hierarchyChanged += IdsByScene.Clear;

        private bool IsSharedInItsScene()
        {
            var scene = gameObject.scene;
            if (!IdsByScene.TryGetValue(scene, out var ids)) IdsByScene[scene] = ids = IdsIn(scene, this);
            if (ids.TryGetValue(entityId, out var holder) && holder != null && holder != this) return true;
            ids[entityId] = this;
            return false;
        }

        private static Dictionary<string, SaveableEntity> IdsIn(Scene scene, SaveableEntity asking)
        {
            var ids = new Dictionary<string, SaveableEntity>(StringComparer.Ordinal);
            foreach (var entity in FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include))
                if (entity != asking && entity.gameObject.scene == scene && !string.IsNullOrEmpty(entity.entityId) && !ids.ContainsKey(entity.entityId))
                    ids.Add(entity.entityId, entity);
            return ids;
        }

        private void ReportDuplicateComponentKeys()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var component in GetComponents<ISaveableComponent>())
                if (!keys.Add(component.Key)) Debug.LogError($"Save: {name} has two saved components keyed '{component.Key}'; one won't be saved.", this);
        }
#endif

        #endregion
    }
}

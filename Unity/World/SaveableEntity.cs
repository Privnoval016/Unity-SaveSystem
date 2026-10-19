using System;
using UnityEngine;
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

        #endregion

        #region MonoBehaviour Callbacks

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (PrefabUtility.IsPartOfPrefabAsset(this) || PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                if (string.IsNullOrEmpty(entityId)) return;
                entityId = string.Empty;
                EditorUtility.SetDirty(this);
                return;
            }
            if (string.IsNullOrEmpty(entityId) || IsSharedInLoadedScenes()) AssignNewId();
        }
#endif

        #endregion

        #region Public API

        /** <summary>Removes the object for good: the scene's save remembers it is gone.</summary> */
        public void Despawn()
        {
            OnDespawned(this);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /** <summary>Gives the entity a new id. Its saved state is left behind under the old one.</summary> */
        [ContextMenu("New Entity Id")]
        public void AssignNewId()
        {
            entityId = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        #endregion

        #region Helpers

#if UNITY_EDITOR
        private bool IsSharedInLoadedScenes()
        {
            foreach (var other in FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other != this && string.Equals(other.entityId, entityId, StringComparison.Ordinal)) return true;
            return false;
        }
#endif

        #endregion
    }
}

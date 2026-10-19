using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SaveSystem
{
    /**
     * <summary>Marks a scene whose entities are saved, and makes its section. Put one in each such scene.</summary>
     * <remarks>
     * The game registers <see cref="CreateSection" />'s result when the scene loads and unregisters it before the scene
     * unloads, which keeps the scene's state held until it loads again.
     * </remarks>
     */
    [DisallowMultipleComponent]
    public sealed class SaveableScene : MonoBehaviour
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor from the scene's GUID. Never edit it: saves point at it. A copied scene needs a new one (context menu).")]
        [SerializeField] private AssetId sceneId;

        #endregion

        #region Private Fields

        private const string KeyPrefix = "world/";

        #endregion

        #region Properties

        /** <summary>The section key this scene saves under.</summary> */
        public string Key => KeyPrefix + sceneId.Value;

        #endregion

        #region MonoBehaviour Callbacks

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!sceneId.IsEmpty || PrefabUtility.IsPartOfPrefabAsset(this) || string.IsNullOrEmpty(gameObject.scene.path)) return;
            if (!AssetId.TryParse(AssetDatabase.AssetPathToGUID(gameObject.scene.path), out sceneId)) return;
            EditorUtility.SetDirty(this);
        }
#endif

        #endregion

        #region Public API

        /** <summary>The saveable scene in a loaded scene, found among its root objects; null when it has none.</summary> */
        public static SaveableScene FindIn(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<SaveableScene>(true);
                if (found != null) return found;
            }
            return null;
        }

        /** <summary>Makes the section for every entity in this scene, as it is now.</summary> */
        public SceneSaveSection CreateSection(SaveSerializer serializer, int restoreOrder = 0, ISaveLog log = null)
        {
            var entities = new List<SaveableEntity>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                entities.AddRange(root.GetComponentsInChildren<SaveableEntity>(true));
            return new SceneSaveSection(Key, entities, serializer, restoreOrder, log);
        }

        /** <summary>Gives the scene a new random id, for a scene copied from another.</summary> */
        [ContextMenu("New Scene Id")]
        public void AssignNewId()
        {
            sceneId = AssetId.New();
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaveSystem.EditorTools
{
    /** <summary>Finds saveable assets in the project and reads and writes their id field.</summary> */
    public static class SaveableAssetDatabase
    {
        #region Public API

        /** <summary>Every saveable ScriptableObject in the project, sub-assets included, in a stable order.</summary> */
        public static List<ScriptableObject> FindAll()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in TypeCache.GetTypesDerivedFrom<ISaveableAsset>())
            {
                if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return LoadAt(paths);
        }

        /** <summary>The saveable ScriptableObjects in the given files, sub-assets included.</summary> */
        public static List<ScriptableObject> LoadAt(IEnumerable<string> paths)
        {
            var found = new List<ScriptableObject>();
            foreach (var path in paths.Where(path => !string.IsNullOrEmpty(path)).Distinct().OrderBy(path => path, StringComparer.Ordinal))
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is ScriptableObject scriptable && SaveableAssets.IsSaveable(scriptable)) found.Add(scriptable);
            return found;
        }

        /** <summary>Describes an asset for the planner.</summary> */
        public static AssetIdRecord RecordOf(ScriptableObject asset, ISet<string> justImported)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            return new AssetIdRecord(path, AssetDatabase.AssetPathToGUID(path), AssetDatabase.IsMainAsset(asset),
                SaveableAssets.IdOf(asset), justImported != null && justImported.Contains(path));
        }

        /** <summary>How many serialized <see cref="AssetId" /> fields the asset has at its top level; exactly one is valid.</summary> */
        public static int CountIdFields(Object asset) => IdFields(new SerializedObject(asset)).Count;

        /**
         * <summary>Writes an id into the asset's one id field and marks it dirty.</summary>
         * <returns>False when the asset doesn't have exactly one id field.</returns>
         */
        public static bool WriteId(Object asset, AssetId id)
        {
            var serialized = new SerializedObject(asset);
            var fields = IdFields(serialized);
            if (fields.Count != 1) return false;
            fields[0].FindPropertyRelative(AssetId.ValueFieldName).stringValue = id.Value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return true;
        }

        #endregion

        #region Helpers

        private static List<SerializedProperty> IdFields(SerializedObject serialized)
        {
            var fields = new List<SerializedProperty>();
            var property = serialized.GetIterator();
            var enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType == SerializedPropertyType.Generic && property.type == nameof(AssetId)) fields.Add(property.Copy());
            }
            return fields;
        }

        #endregion
    }
}

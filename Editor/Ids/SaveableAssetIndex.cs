using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveSystem.EditorTools
{
    /** <summary>Which files hold which asset ids, kept current as assets are imported, moved and deleted, so a change never needs a whole-project scan.</summary> */
    public sealed class SaveableAssetIndex
    {
        #region Private Fields

        /** <summary>Keyed by asset path, resolving to the ids of the saveable assets in that file.</summary> */
        private readonly Dictionary<string, List<AssetId>> _idsByPath = new Dictionary<string, List<AssetId>>(StringComparer.Ordinal);

        /** <summary>Keyed by asset id, resolving to the files holding an asset with that id.</summary> */
        private readonly Dictionary<AssetId, HashSet<string>> _pathsById = new Dictionary<AssetId, HashSet<string>>();

        #endregion

        #region Public API

        /** <summary>Records the ids now held by the file at a path, replacing what was recorded for it.</summary> */
        public void Record(string path, IEnumerable<AssetId> ids)
        {
            Forget(path);
            var held = ids.Where(id => !id.IsEmpty).ToList();
            if (held.Count == 0) return;
            _idsByPath[path] = held;
            foreach (var id in held)
            {
                if (!_pathsById.TryGetValue(id, out var paths)) _pathsById[id] = paths = new HashSet<string>(StringComparer.Ordinal);
                paths.Add(path);
            }
        }

        /** <summary>Forgets a file, such as one deleted or moved away.</summary> */
        public void Forget(string path)
        {
            if (!_idsByPath.TryGetValue(path, out var ids)) return;
            _idsByPath.Remove(path);
            foreach (var id in ids)
            {
                if (!_pathsById.TryGetValue(id, out var paths)) continue;
                paths.Remove(path);
                if (paths.Count == 0) _pathsById.Remove(id);
            }
        }

        /** <summary>True when some file holds an asset with this id.</summary> */
        public bool Contains(AssetId id) => _pathsById.ContainsKey(id);

        /** <summary>Every file holding an asset with any of these ids.</summary> */
        public IEnumerable<string> PathsHolding(IEnumerable<AssetId> ids) =>
            ids.Where(id => _pathsById.ContainsKey(id)).SelectMany(id => _pathsById[id]).Distinct(StringComparer.Ordinal);

        #endregion
    }
}

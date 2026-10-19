using System;
using System.Collections.Generic;

namespace SaveSystem
{
    /** <summary>A load whose data has been upgraded and read for every section, waiting to be applied.</summary> */
    public sealed class PreparedLoad
    {
        #region Private Fields

        private readonly List<PreparedSection> _sections = new List<PreparedSection>();
        private bool _applied;

        #endregion

        #region Properties

        /** <summary>Why the load can't be applied; null when it can.</summary> */
        public LoadResult Failure { get; }

        /** <summary>True when every section's data was read and the load has not been applied yet.</summary> */
        public bool CanApply => Failure == null && !_applied;

        /** <summary>The document after its document upgrades.</summary> */
        internal SaveDocument Document { get; }

        /** <summary>The prepared sections, in restore order.</summary> */
        internal IReadOnlyList<PreparedSection> Sections => _sections;

        #endregion

        #region Public API

        internal PreparedLoad(SaveDocument document) => Document = document;

        internal PreparedLoad(LoadResult failure) => Failure = failure;

        internal void Add(PreparedSection section) => _sections.Add(section);

        internal void MarkApplied()
        {
            if (Failure != null) throw new InvalidOperationException("This load failed to prepare: " + Failure);
            if (_applied) throw new InvalidOperationException("This load was already applied.");
            _applied = true;
        }

        #endregion
    }
}

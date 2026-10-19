using System;
using System.Collections.Generic;

namespace SaveSystem
{
    /** <summary>A load whose data has been upgraded and read for every section, waiting to be applied.</summary> */
    public sealed class PreparedLoad
    {
        #region Private Fields

        private readonly List<ISaveSection> _sections = new List<ISaveSection>();
        private readonly List<IPreparedRestore> _restores = new List<IPreparedRestore>();
        private bool _applied;

        #endregion

        #region Properties

        /** <summary>Why the load can't be applied; null when it can.</summary> */
        public LoadResult Failure { get; }

        /** <summary>True when every section's data was read and the load has not been applied yet.</summary> */
        public bool CanApply => Failure == null && !_applied;

        internal SaveDocument Document { get; }
        internal IReadOnlyList<ISaveSection> Sections => _sections;
        internal IReadOnlyList<IPreparedRestore> Restores => _restores;
        internal List<SectionReport> Reports { get; } = new List<SectionReport>();

        #endregion

        #region Public API

        internal PreparedLoad(SaveDocument document) => Document = document;

        internal PreparedLoad(LoadResult failure) => Failure = failure;

        internal void Add(ISaveSection section, IPreparedRestore restore, SectionReport report)
        {
            _sections.Add(section);
            _restores.Add(restore);
            Reports.Add(report);
        }

        internal void MarkApplied()
        {
            if (Failure != null) throw new InvalidOperationException("This load failed to prepare: " + Failure);
            if (_applied) throw new InvalidOperationException("This load was already applied.");
            _applied = true;
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>Holds the registered sections and turns live state into a <see cref="SaveDocument" /> and back.</summary>
     * <remarks>
     * Main thread only. Loading is all or nothing: every section's data is upgraded and read before any is applied.
     * Data whose system is not registered is held and written back, and is restored when that system registers.
     * </remarks>
     */
    public sealed class SaveService
    {
        #region Private Systems

        private readonly ISaveLog _log;

        #endregion

        #region Private Fields

        private readonly List<ISaveSection> _sections = new List<ISaveSection>();

        /** <summary>Keyed by section key, resolving to data whose system is not registered.</summary> */
        private readonly Dictionary<string, SectionRecord> _held = new Dictionary<string, SectionRecord>(StringComparer.Ordinal);

        #endregion

        #region Properties

        /** <summary>The JSON rules sections are captured and read with.</summary> */
        public SaveSerializer Serializer { get; }

        /** <summary>The keys of data held for systems that are not registered.</summary> */
        public IReadOnlyCollection<string> HeldKeys => _held.Keys;

        #endregion

        #region Public API

        /** <summary>Makes a service with no sections.</summary> */
        public SaveService(SaveSerializer serializer = null, ISaveLog log = null)
        {
            Serializer = serializer ?? new SaveSerializer();
            _log = log ?? SilentSaveLog.Instance;
        }

        /** <summary>True when a section with this key is registered.</summary> */
        public bool IsRegistered(string key) => FindSection(key) != null;

        /**
         * <summary>Adds a section. If data is held for its key, the section is restored from it at once.</summary>
         * <returns>
         * <see cref="SectionOutcome.Unchanged" /> when nothing was held. <see cref="SectionOutcome.Failed" /> when the held
         * data could not be used: the section is then not registered and the data stays held.
         * </returns>
         * <exception cref="ArgumentException">The key, version or upgrades are invalid.</exception>
         * <exception cref="InvalidOperationException">A section with this key is already registered.</exception>
         */
        public SectionReport Register(ISaveSection section)
        {
            Validate(section);
            if (IsRegistered(section.Key)) throw new InvalidOperationException($"A section with key '{section.Key}' is already registered.");

            if (!_held.TryGetValue(section.Key, out var record))
            {
                _sections.Add(section);
                return new SectionReport(section.Key, SectionOutcome.Unchanged, 0);
            }

            if (!TryPrepare(section, record, out var prepared, out var report, out _))
            {
                _log.Error($"Save section '{section.Key}' not registered: {report.Problem}");
                return report;
            }

            _sections.Add(section);
            _held.Remove(section.Key);
            return TryApply(section, prepared, report);
        }

        /** <summary>Removes a section, keeping its current state held so it is saved and restored on the next register.</summary> */
        public void Unregister(ISaveSection section)
        {
            if (section == null || !_sections.Remove(section)) return;
            try
            {
                _held[section.Key] = new SectionRecord(section.Version, section.Capture(Serializer.Json));
            }
            catch (Exception e)
            {
                _log.Error($"Save section '{section.Key}' failed to capture on unregister: {e.Message}");
            }
        }

        /**
         * <summary>Captures every registered section, plus the held data, into a new document.</summary>
         * <exception cref="SectionCaptureException">A section threw.</exception>
         */
        public SaveDocument Capture(JObject meta = null)
        {
            var document = new SaveDocument { Meta = meta };
            foreach (var pair in _held)
                document.Sections[pair.Key] = pair.Value;
            foreach (var section in _sections)
                document.Sections[section.Key] = new SectionRecord(section.Version, CaptureSection(section));
            return document;
        }

        /** <summary>Restores every registered section from a document; nothing changes unless every section can be read.</summary> */
        public LoadResult Restore(SaveDocument document)
        {
            var prepared = Prepare(document);
            return prepared.CanApply ? Apply(prepared) : prepared.Failure;
        }

        /**
         * <summary>Upgrades and reads every registered section's data from a document without changing anything.</summary>
         * <returns>A load to <see cref="Apply" />, or the reason it can't be.</returns>
         */
        public PreparedLoad Prepare(SaveDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.Format > SaveDocument.CurrentFormat)
                return new PreparedLoad(new LoadResult(LoadStatus.TooNew, problem: $"Save format {document.Format} is newer than {SaveDocument.CurrentFormat}."));

            var load = new PreparedLoad(document);
            foreach (var section in InRestoreOrder())
            {
                if (!TryPrepareFrom(document, section, out var restore, out var report, out var tooNew))
                    return new PreparedLoad(new LoadResult(tooNew ? LoadStatus.TooNew : LoadStatus.SectionFailed, new[] { report }, report.ToString()));
                load.Add(section, restore, report);
            }
            return load;
        }

        /**
         * <summary>Applies a prepared load to live state, and holds the document's data for unregistered systems.</summary>
         * <exception cref="InvalidOperationException">The load failed to prepare, or was already applied.</exception>
         */
        public LoadResult Apply(PreparedLoad load)
        {
            if (load == null) throw new ArgumentNullException(nameof(load));
            load.MarkApplied();
            var failed = ApplyAll(load.Sections, load.Restores, load.Reports);
            HoldUnregistered(load.Document, load.Reports);
            return failed == null
                ? new LoadResult(LoadStatus.Loaded, load.Reports)
                : new LoadResult(LoadStatus.RestoreFailed, load.Reports, failed);
        }

        /** <summary>Forgets held data and gives every registered section a new game's state.</summary> */
        public LoadResult NewGame()
        {
            var ordered = InRestoreOrder();
            var prepared = new List<IPreparedRestore>(ordered.Count);
            var reports = new List<SectionReport>(ordered.Count);
            foreach (var section in ordered)
            {
                if (!TryPrepareDefault(section, out var restore, out var report))
                    return new LoadResult(LoadStatus.SectionFailed, new[] { report }, report.ToString());
                prepared.Add(restore);
                reports.Add(report);
            }

            _held.Clear();
            var failed = ApplyAll(ordered, prepared, reports);
            return failed == null
                ? new LoadResult(LoadStatus.Loaded, reports)
                : new LoadResult(LoadStatus.RestoreFailed, reports, failed);
        }

        #endregion

        #region Helpers

        private ISaveSection FindSection(string key)
        {
            foreach (var section in _sections)
                if (string.Equals(section.Key, key, StringComparison.Ordinal)) return section;
            return null;
        }

        private static void Validate(ISaveSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (!SaveNames.IsValidSectionKey(section.Key)) throw new ArgumentException($"Invalid section key '{section.Key}'.", nameof(section));
            if (section.Version < 1) throw new ArgumentException($"Section '{section.Key}' has version {section.Version}; versions start at 1.", nameof(section));

            var seen = new HashSet<int>();
            foreach (var upgrade in section.Upgrades ?? Array.Empty<SectionUpgrade>())
            {
                if (upgrade == null) throw new ArgumentException($"Section '{section.Key}' has a null upgrade.", nameof(section));
                if (upgrade.FromVersion >= section.Version)
                    throw new ArgumentException($"Section '{section.Key}' has an upgrade from version {upgrade.FromVersion}, not below its version {section.Version}.", nameof(section));
                if (!seen.Add(upgrade.FromVersion))
                    throw new ArgumentException($"Section '{section.Key}' has two upgrades from version {upgrade.FromVersion}.", nameof(section));
            }
        }

        private List<ISaveSection> InRestoreOrder() => _sections.OrderBy(section => section.RestoreOrder).ToList();

        private JToken CaptureSection(ISaveSection section)
        {
            try
            {
                return section.Capture(Serializer.Json) ?? JValue.CreateNull();
            }
            catch (Exception e)
            {
                throw new SectionCaptureException(section.Key, e);
            }
        }

        private bool TryPrepareFrom(SaveDocument document, ISaveSection section, out IPreparedRestore prepared, out SectionReport report, out bool tooNew)
        {
            tooNew = false;
            return document.Sections.TryGetValue(section.Key, out var record)
                ? TryPrepare(section, record, out prepared, out report, out tooNew)
                : TryPrepareDefault(section, out prepared, out report);
        }

        private bool TryPrepare(ISaveSection section, SectionRecord record, out IPreparedRestore prepared, out SectionReport report, out bool tooNew)
        {
            prepared = null;
            tooNew = record.Version > section.Version;
            if (tooNew)
            {
                report = Failed(section, record, $"stored version {record.Version} is newer than {section.Version}");
                return false;
            }

            var data = record.Data;
            for (var version = record.Version; version < section.Version; version++)
            {
                var upgrade = FindUpgrade(section, version);
                if (upgrade == null)
                {
                    report = Failed(section, record, $"no upgrade from version {version}");
                    return false;
                }
                try
                {
                    data = upgrade.Apply(data);
                }
                catch (Exception e)
                {
                    report = Failed(section, record, $"upgrade from version {version} threw: {e.Message}");
                    return false;
                }
            }

            try
            {
                prepared = section.Prepare(data, Serializer.Json);
            }
            catch (Exception e)
            {
                report = Failed(section, record, $"data could not be read: {e.Message}");
                return false;
            }

            var outcome = record.Version == section.Version ? SectionOutcome.Restored : SectionOutcome.Upgraded;
            report = new SectionReport(section.Key, outcome, record.Version);
            return true;
        }

        private static bool TryPrepareDefault(ISaveSection section, out IPreparedRestore prepared, out SectionReport report)
        {
            try
            {
                prepared = section.PrepareDefault();
                report = new SectionReport(section.Key, SectionOutcome.Defaulted, 0);
                return true;
            }
            catch (Exception e)
            {
                prepared = null;
                report = new SectionReport(section.Key, SectionOutcome.Failed, 0, $"default could not be made: {e.Message}");
                return false;
            }
        }

        private static SectionUpgrade FindUpgrade(ISaveSection section, int fromVersion)
        {
            foreach (var upgrade in section.Upgrades ?? Array.Empty<SectionUpgrade>())
                if (upgrade.FromVersion == fromVersion) return upgrade;
            return null;
        }

        private static SectionReport Failed(ISaveSection section, SectionRecord record, string problem) =>
            new SectionReport(section.Key, SectionOutcome.Failed, record.Version, problem);

        private string ApplyAll(IReadOnlyList<ISaveSection> sections, IReadOnlyList<IPreparedRestore> prepared, List<SectionReport> reports)
        {
            string failed = null;
            for (var i = 0; i < sections.Count; i++)
            {
                reports[i] = TryApply(sections[i], prepared[i], reports[i]);
                if (reports[i].Outcome == SectionOutcome.Failed) failed = failed == null ? reports[i].ToString() : $"{failed}; {reports[i]}";
            }
            return failed;
        }

        private SectionReport TryApply(ISaveSection section, IPreparedRestore prepared, SectionReport report)
        {
            try
            {
                prepared.Apply();
                return report;
            }
            catch (Exception e)
            {
                _log.Error($"Save section '{section.Key}' threw while restoring: {e.Message}");
                return new SectionReport(section.Key, SectionOutcome.Failed, report.StoredVersion, $"restore threw: {e.Message}");
            }
        }

        private void HoldUnregistered(SaveDocument document, List<SectionReport> reports)
        {
            _held.Clear();
            foreach (var pair in document.Sections)
            {
                if (IsRegistered(pair.Key)) continue;
                _held[pair.Key] = pair.Value;
                reports.Add(new SectionReport(pair.Key, SectionOutcome.Held, pair.Value.Version));
            }
        }

        #endregion
    }
}

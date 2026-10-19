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
        private readonly List<DocumentUpgrade> _documentUpgrades = new List<DocumentUpgrade>();

        /** <summary>Keyed by section key, resolving to data whose system is not registered.</summary> */
        private readonly Dictionary<string, SectionRecord> _held = new Dictionary<string, SectionRecord>(StringComparer.Ordinal);

        /** <summary>Document upgrades the loaded save had, kept even when this build no longer has them.</summary> */
        private readonly SortedSet<string> _appliedUpgrades = new SortedSet<string>(StringComparer.Ordinal);

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
         * <summary>Adds an upgrade that runs once on every save that hasn't had it, before sections read their data.</summary>
         * <exception cref="InvalidOperationException">An upgrade with this id was already added.</exception>
         */
        public void AddDocumentUpgrade(DocumentUpgrade upgrade)
        {
            if (upgrade == null) throw new ArgumentNullException(nameof(upgrade));
            if (_documentUpgrades.Any(added => added.Id == upgrade.Id)) throw new InvalidOperationException($"Document upgrade '{upgrade.Id}' was already added.");
            _documentUpgrades.Add(upgrade);
        }

        /**
         * <summary>Adds a section. If data is held for its key, the section is restored from it at once.</summary>
         * <returns>
         * <see cref="SectionOutcome.Unchanged" /> when nothing was held. <see cref="SectionOutcome.Failed" /> when the held
         * data could not be used or restoring threw: the section is then not registered and the data stays held.
         * </returns>
         * <exception cref="ArgumentException">The key, version or upgrades are invalid.</exception>
         * <exception cref="InvalidOperationException">A section with this key is already registered.</exception>
         */
        public SectionReport Register(ISaveSection section)
        {
            SectionPreparer.Validate(section);
            if (IsRegistered(section.Key)) throw new InvalidOperationException($"A section with key '{section.Key}' is already registered.");

            _sections.Add(section);
            if (!_held.TryGetValue(section.Key, out var record)) return new SectionReport(section.Key, SectionOutcome.Unchanged, SectionReport.NoVersion);
            return RestoreRegistered(section, record);
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
        public SaveDocument Capture()
        {
            var document = new SaveDocument();
            document.AppliedUpgrades.UnionWith(_appliedUpgrades);
            document.AppliedUpgrades.UnionWith(_documentUpgrades.Select(upgrade => upgrade.Id));
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
         * <summary>Runs document upgrades on a copy, then upgrades and reads every registered section's data, changing nothing.</summary>
         * <returns>A load to <see cref="Apply" />, or the reason it can't be.</returns>
         */
        public PreparedLoad Prepare(SaveDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.Format > SaveDocument.CurrentFormat)
                return new PreparedLoad(new LoadResult(LoadStatus.TooNew, problem: $"Save format {document.Format} is newer than {SaveDocument.CurrentFormat}."));

            var working = document.Copy();
            var upgradeFailure = RunDocumentUpgrades(working);
            if (upgradeFailure != null) return new PreparedLoad(new LoadResult(LoadStatus.SectionFailed, problem: upgradeFailure));

            var load = new PreparedLoad(working);
            foreach (var section in InRestoreOrder())
            {
                var prepared = PrepareFrom(working, section);
                if (!prepared.Succeeded)
                    return new PreparedLoad(new LoadResult(prepared.TooNew ? LoadStatus.TooNew : LoadStatus.SectionFailed, new[] { prepared.Report }, prepared.Report.ToString()));
                load.Add(prepared);
            }
            return load;
        }

        /**
         * <summary>Applies a prepared load to live state. Sections registered since it was prepared are restored too,
         * and the document's data for unregistered systems is held.</summary>
         * <exception cref="InvalidOperationException">The load failed to prepare, or was already applied.</exception>
         */
        public LoadResult Apply(PreparedLoad load)
        {
            if (load == null) throw new ArgumentNullException(nameof(load));
            load.MarkApplied();

            var reports = load.Sections.Select(TryApply).ToList();
            _held.Clear();
            foreach (var section in _sections.Where(section => load.Sections.All(prepared => prepared.Section != section)).ToList())
                if (load.Document.Sections.TryGetValue(section.Key, out var record)) reports.Add(RestoreRegistered(section, record));
            HoldUnregistered(load.Document, reports);

            _appliedUpgrades.Clear();
            _appliedUpgrades.UnionWith(load.Document.AppliedUpgrades);
            return LoadResult.FromReports(reports);
        }

        /** <summary>Forgets held data and gives every registered section a new game's state.</summary> */
        public LoadResult NewGame()
        {
            var prepared = InRestoreOrder().Select(SectionPreparer.Default).ToList();
            var failed = prepared.FirstOrDefault(section => !section.Succeeded);
            if (failed.Section != null) return new LoadResult(LoadStatus.SectionFailed, new[] { failed.Report }, failed.Report.ToString());

            _held.Clear();
            _appliedUpgrades.Clear();
            return LoadResult.FromReports(prepared.Select(TryApply).ToList());
        }

        #endregion

        #region Helpers

        private ISaveSection FindSection(string key)
        {
            foreach (var section in _sections)
                if (string.Equals(section.Key, key, StringComparison.Ordinal)) return section;
            return null;
        }

        private List<ISaveSection> InRestoreOrder() => _sections.OrderBy(section => section.RestoreOrder).ToList();

        private JToken CaptureSection(ISaveSection section)
        {
            try
            {
                return section.Capture(Serializer.Json);
            }
            catch (Exception e)
            {
                throw new SectionCaptureException(section.Key, e);
            }
        }

        private string RunDocumentUpgrades(SaveDocument document)
        {
            foreach (var upgrade in _documentUpgrades)
            {
                if (document.AppliedUpgrades.Contains(upgrade.Id)) continue;
                try
                {
                    upgrade.Apply(document);
                }
                catch (Exception e)
                {
                    return $"document upgrade '{upgrade.Id}' threw: {e.Message}";
                }
                document.AppliedUpgrades.Add(upgrade.Id);
            }
            return null;
        }

        private PreparedSection PrepareFrom(SaveDocument document, ISaveSection section) =>
            document.Sections.TryGetValue(section.Key, out var record)
                ? SectionPreparer.FromRecord(section, record, Serializer.Json)
                : SectionPreparer.Default(section);

        private SectionReport RestoreRegistered(ISaveSection section, SectionRecord record)
        {
            var prepared = SectionPreparer.FromRecord(section, record, Serializer.Json);
            var report = prepared.Succeeded ? TryApply(prepared) : prepared.Report;
            if (report.Outcome == SectionOutcome.Failed)
            {
                _sections.Remove(section);
                _held[section.Key] = record;
                _log.Error($"Save section '{section.Key}' not registered: {report.Problem}");
                return report;
            }
            _held.Remove(section.Key);
            return report;
        }

        private SectionReport TryApply(PreparedSection prepared)
        {
            try
            {
                prepared.Restore.Apply();
                return prepared.Report;
            }
            catch (Exception e)
            {
                _log.Error($"Save section '{prepared.Section.Key}' threw while restoring: {e.Message}");
                return new SectionReport(prepared.Section.Key, SectionOutcome.Failed, prepared.Report.StoredVersion, $"restore threw: {e.Message}");
            }
        }

        private void HoldUnregistered(SaveDocument document, List<SectionReport> reports)
        {
            foreach (var pair in document.Sections)
            {
                if (IsRegistered(pair.Key) && !_held.ContainsKey(pair.Key)) continue;
                _held[pair.Key] = pair.Value;
                if (reports.All(report => report.Key != pair.Key)) reports.Add(new SectionReport(pair.Key, SectionOutcome.Held, pair.Value.Version));
            }
        }

        #endregion
    }
}

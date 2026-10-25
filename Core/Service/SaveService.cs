using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /**
     * <summary>Holds the registered sections and turns live state into a <see cref="SaveDocument" /> and back.</summary>
     * <remarks>
     * Main thread only. Loading is all or nothing: every section's data is upgraded and read before any is applied, and
     * if a section then throws while applying or settling, every section is put back as it was before the load. Data
     * whose system is not registered is held and written back, and is restored when that system registers.
     * </remarks>
     */
    public sealed class SaveService
    {
        #region Private Systems

        private readonly ISaveLog _log;

        #endregion

        #region Private Fields

        private readonly List<ISaveSection> _sections = new List<ISaveSection>();

        /** <summary>Keyed by section key, resolving to the registered section.</summary> */
        private readonly Dictionary<string, ISaveSection> _byKey = new Dictionary<string, ISaveSection>(StringComparer.Ordinal);
        private readonly List<DocumentUpgrade> _documentUpgrades = new List<DocumentUpgrade>();

        /** <summary>Keyed by section key, resolving to data whose system is not registered.</summary> */
        private readonly Dictionary<string, SectionRecord> _held = new Dictionary<string, SectionRecord>(StringComparer.Ordinal);

        /** <summary>Document upgrades the loaded save had, kept even when this build no longer has them.</summary> */
        private readonly SortedSet<string> _appliedUpgrades = new SortedSet<string>(StringComparer.Ordinal);

        /**
         * <summary>Keyed by section key, resolving to the data a registered section is saved with instead of its live
         * state: its restore failed and could not be put back, so its live state is not trusted. A load or new game that
         * restores it clears it.</summary>
         */
        private readonly Dictionary<string, SectionRecord> _pinned = new Dictionary<string, SectionRecord>(StringComparer.Ordinal);

        /** <summary>Keyed by registered section key, resolving to its version: a copy replaced on every registration, so a background job can read it while sections come and go.</summary> */
        private volatile Dictionary<string, int> _versions = new Dictionary<string, int>(StringComparer.Ordinal);

        /** <summary>Keyed by section key, resolving to the data a registered section was last restored from or captured as: kept for one that fails to capture as it unregisters.</summary> */
        private readonly Dictionary<string, SectionRecord> _last = new Dictionary<string, SectionRecord>(StringComparer.Ordinal);

        #endregion

        #region Properties

        /** <summary>The JSON rules sections are captured and read with.</summary> */
        public SaveSerializer Serializer { get; }

        /** <summary>The keys of data held for systems that are not registered.</summary> */
        public IReadOnlyCollection<string> HeldKeys => _held.Keys;

        /** <summary>The keys of registered sections saved with their last good data instead of their live state, after a restore that failed and could not be put back.</summary> */
        public IReadOnlyCollection<string> PinnedKeys => _pinned.Keys;

        #endregion

        #region Public API

        /** <summary>Makes a service with no sections.</summary> */
        public SaveService(SaveSerializer serializer = null, ISaveLog log = null)
        {
            Serializer = serializer ?? new SaveSerializer();
            _log = log ?? SilentSaveLog.Instance;
        }

        /** <summary>True when a section with this key is registered.</summary> */
        public bool IsRegistered(string key) => key != null && _byKey.ContainsKey(key);

        /**
         * <summary>True when a saved document's JSON is from a newer build than this one: a newer document format, or a
         * registered section's data at a version above its own. Such a save loads as <see cref="LoadStatus.TooNew" /> and
         * must never be written over. Safe on any thread: it reads a copy of the registered versions.</summary>
         */
        public bool IsFromNewerBuild(JToken document)
        {
            if (!(document is JObject root)) return false;
            if ((root.Value<int?>(SaveDocument.FormatKey) ?? 0) > SaveDocument.CurrentFormat) return true;
            if (!(root[SaveDocument.SectionsKey] is JObject sections)) return false;
            foreach (var pair in _versions)
                if (sections[pair.Key] is JObject record && (record.Value<int?>(SaveDocument.VersionKey) ?? 0) > pair.Value) return true;
            return false;
        }

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

            Add(section);
            if (!_held.TryGetValue(section.Key, out var record)) return new SectionReport(section.Key, SectionOutcome.Unchanged, SectionReport.NoVersion);
            return RestoreRegistered(section, record, true);
        }

        /**
         * <summary>Removes a section, keeping its current state held so it is saved and restored on the next register. A
         * section that fails to capture keeps the state it was last restored with or saved as. With
         * <paramref name="keepState" /> false its state is forgotten instead (an area left whose world resets): the next
         * save has none, and it starts afresh when it registers again.</summary>
         */
        public void Unregister(ISaveSection section, bool keepState = true)
        {
            if (section == null || !Remove(section)) return;
            var key = section.Key;
            if (!keepState)
            {
                _held.Remove(key);
                _pinned.Remove(key);
                _last.Remove(key);
                return;
            }
            if (_pinned.TryGetValue(key, out var pinned))
                _held[key] = pinned;
            else
            {
                try
                {
                    _held[key] = new SectionRecord(section.Version, CaptureSection(section));
                }
                catch (SectionCaptureException e)
                {
                    if (_last.TryGetValue(key, out var last)) _held[key] = last;
                    _log.Error($"Save section '{key}' failed to capture on unregister{(_last.ContainsKey(key) ? "; its last saved state is kept" : "")}: {e.InnerException?.Message}");
                }
            }
            _pinned.Remove(key);
            _last.Remove(key);
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
            {
                if (_pinned.TryGetValue(section.Key, out var pinned))
                {
                    document.Sections[section.Key] = pinned;
                    continue;
                }
                var record = new SectionRecord(section.Version, CaptureSection(section));
                document.Sections[section.Key] = record;
                _last[section.Key] = record;
            }
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
            var upgradeFailure = RunDocumentUpgrades(ref working);
            if (upgradeFailure != null) return new PreparedLoad(new LoadResult(LoadStatus.UpgradeFailed, problem: upgradeFailure));

            var load = new PreparedLoad(working);
            foreach (var section in _sections)
            {
                var prepared = PrepareFrom(working, section);
                if (!prepared.Succeeded)
                    return new PreparedLoad(new LoadResult(prepared.Failure, new[] { prepared.Report }, prepared.Report.ToString()));
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
            var before = TrySnapshot();
            var upgradesBefore = new List<string>(_appliedUpgrades);

            var reports = load.Sections.Where(IsStillRegistered).Select(TryApply).ToList();
            _held.Clear();
            var prepared = new HashSet<ISaveSection>(load.Sections.Select(section => section.Section));
            foreach (var section in _sections.Where(section => !prepared.Contains(section)).ToList())
            {
                if (load.Document.Sections.TryGetValue(section.Key, out var record))
                {
                    reports.Add(RestoreRegistered(section, record, false));
                    continue;
                }
                var defaulted = SectionPreparer.Default(section);
                reports.Add(ApplyIfPrepared(defaulted));
            }
            HoldUnregistered(load.Document, reports);
            _appliedUpgrades.Clear();
            _appliedUpgrades.UnionWith(load.Document.AppliedUpgrades);
            SettleAll(reports);

            var result = LoadResult.FromReports(reports);
            if (!NeedsRollBack(reports))
            {
                Trust(load.Document);
                return result;
            }
            return RollBack(before, upgradesBefore, result, load.Document);
        }

        /** <summary>Forgets held data and gives every registered section a new game's state; a section that throws puts every section back as it was.</summary> */
        public LoadResult NewGame()
        {
            var prepared = _sections.Select(SectionPreparer.Default).ToList();
            if (prepared.Any(section => !section.Succeeded))
            {
                var failed = prepared.First(section => !section.Succeeded);
                return new LoadResult(LoadStatus.SectionFailed, new[] { failed.Report }, failed.Report.ToString());
            }

            var before = TrySnapshot();
            var upgradesBefore = new List<string>(_appliedUpgrades);
            var heldBefore = new Dictionary<string, SectionRecord>(_held, StringComparer.Ordinal);
            _held.Clear();
            _appliedUpgrades.Clear();
            var reports = prepared.Select(TryApply).ToList();
            SettleAll(reports);

            var result = LoadResult.FromReports(reports);
            if (!NeedsRollBack(reports))
            {
                Trust(null);
                return result;
            }
            if (before == null)
                foreach (var pair in heldBefore) _held[pair.Key] = pair.Value;
            return RollBack(before, upgradesBefore, result, null);
        }

        #endregion

        #region Helpers

        private void Add(ISaveSection section)
        {
            _sections.Add(section);
            _byKey.Add(section.Key, section);
            _versions = new Dictionary<string, int>(_versions, StringComparer.Ordinal) { [section.Key] = section.Version };
        }

        private bool Remove(ISaveSection section)
        {
            if (!_byKey.TryGetValue(section.Key, out var registered) || registered != section) return false;
            _byKey.Remove(section.Key);
            _sections.Remove(section);
            var versions = new Dictionary<string, int>(_versions, StringComparer.Ordinal);
            versions.Remove(section.Key);
            _versions = versions;
            return true;
        }

        /** <summary>The section's state as JSON in canonical order (<see cref="SaveSerializer.Canonical" />), so the same state always writes the same bytes.</summary> */
        private JToken CaptureSection(ISaveSection section)
        {
            try
            {
                var json = section.Capture(Serializer.Json);
                return SaveSerializer.Canonical(json != null && json.Parent != null ? json.DeepClone() : json);
            }
            catch (Exception e)
            {
                throw new SectionCaptureException(section.Key, e);
            }
        }

        private string RunDocumentUpgrades(ref SaveDocument document)
        {
            foreach (var upgrade in _documentUpgrades)
            {
                if (document.AppliedUpgrades.Contains(upgrade.Id)) continue;
                try
                {
                    upgrade.Apply(document);
                    document.AppliedUpgrades.Add(upgrade.Id);
                    document = document.Copy();
                }
                catch (Exception e)
                {
                    return $"document upgrade '{upgrade.Id}' failed: {e.Message}";
                }
            }
            return null;
        }

        private bool IsStillRegistered(PreparedSection prepared) =>
            _byKey.TryGetValue(prepared.Section.Key, out var registered) && registered == prepared.Section;

        private PreparedSection PrepareFrom(SaveDocument document, ISaveSection section) =>
            document.Sections.TryGetValue(section.Key, out var record)
                ? SectionPreparer.FromRecord(section, record, Serializer.Json)
                : SectionPreparer.Default(section);

        /** <summary>Restores a section from held data; on failure it is unregistered and the data stays held. <paramref name="settle" /> settles every section after it (a registration; a load settles once at its end).</summary> */
        private SectionReport RestoreRegistered(ISaveSection section, SectionRecord record, bool settle)
        {
            var prepared = SectionPreparer.FromRecord(section, record, Serializer.Json);
            var report = ApplyIfPrepared(prepared);
            if (report.Outcome == SectionOutcome.Failed)
            {
                Remove(section);
                _held[section.Key] = record;
                _log.Error($"Save section '{section.Key}' not registered: {report.Problem}");
                return report;
            }
            _held.Remove(section.Key);
            _last[section.Key] = record;
            if (!settle) return report;
            var settled = new List<SectionReport>();
            SettleAll(settled);
            if (settled.Count == 0) return report;

            Remove(section);
            _held[section.Key] = record;
            _last.Remove(section.Key);
            var problem = string.Join("; ", settled.Select(failure => failure.ToString()));
            _log.Error($"Save section '{section.Key}' not registered: settling after its restore threw ({problem}); its data stays held.");
            SettleAll(new List<SectionReport>());
            return new SectionReport(section.Key, SectionOutcome.Failed, record.Version, $"settle threw: {problem}");
        }

        /** <summary>A capture of everything as it stands, to put back if a restore fails; null when a section can't capture.</summary> */
        private SaveDocument TrySnapshot()
        {
            try
            {
                return Capture();
            }
            catch (SectionCaptureException e)
            {
                _log.Warning($"Save: the state before this restore could not be captured, so a failure can't be put back: {e.Message}");
                return null;
            }
        }

        /** <summary>Settles every registered section that settles, in registration order; each throw is added as a failed report.</summary> */
        private void SettleAll(List<SectionReport> reports)
        {
            foreach (var section in _sections.ToList())
            {
                if (!(section is ISettlesAfterRestore settling)) continue;
                try
                {
                    settling.Settle();
                }
                catch (Exception e)
                {
                    _log.Error($"Save section '{section.Key}' threw while settling: {e.Message}");
                    reports.Add(new SectionReport(section.Key, SectionOutcome.Failed, SectionReport.NoVersion, $"settle threw: {e.Message}"));
                }
            }
        }

        /**
         * <summary>True when a section still registered failed to apply or settle. A section registered during a load
         * whose data failed has already unregistered with its data held, which leaves everything else consistent.</summary>
         */
        private bool NeedsRollBack(IEnumerable<SectionReport> reports) =>
            reports.Any(report => report.Outcome == SectionOutcome.Failed && IsRegistered(report.Key));

        /** <summary>After a restore that worked: live state is trusted again, and each section's last record is what it was restored from.</summary> */
        private void Trust(SaveDocument restoredFrom)
        {
            _pinned.Clear();
            _last.Clear();
            if (restoredFrom == null) return;
            foreach (var section in _sections)
                if (restoredFrom.Sections.TryGetValue(section.Key, out var record)) _last[section.Key] = record;
        }

        /**
         * <summary>Puts every section back as <paramref name="before" /> had it after a restore failed. A section that
         * can't be put back (or every failed one, with no snapshot) is pinned to its last good data, so no save writes
         * its half-restored state.</summary>
         */
        private LoadResult RollBack(SaveDocument before, List<string> upgradesBefore, LoadResult failed, SaveDocument loading)
        {
            if (before == null)
            {
                foreach (var report in failed.Sections)
                {
                    if (!IsRegistered(report.Key)) continue;
                    if (report.Outcome != SectionOutcome.Failed) _pinned.Remove(report.Key);
                    else if (loading != null && loading.Sections.TryGetValue(report.Key, out var record)) _pinned[report.Key] = record;
                    else if (_last.TryGetValue(report.Key, out var last)) _pinned[report.Key] = last;
                }
                _log.Error($"Save: a restore failed and nothing could be put back; failed sections are saved with their saved data. {failed.Problem}");
                return failed.WithRollback(false, "the state before could not be captured, so it was not put back");
            }

            var problems = new List<string>();
            _held.Clear();
            _pinned.Clear();
            foreach (var section in _sections.ToList())
            {
                var prepared = before.Sections.TryGetValue(section.Key, out var record)
                    ? SectionPreparer.FromRecord(section, record, Serializer.Json)
                    : SectionPreparer.Default(section);
                var report = ApplyIfPrepared(prepared);
                if (report.Outcome != SectionOutcome.Failed) continue;
                problems.Add(report.ToString());
                if (record != null) _pinned[section.Key] = record;
            }
            foreach (var pair in before.Sections)
                if (!IsRegistered(pair.Key)) _held[pair.Key] = pair.Value;
            _appliedUpgrades.Clear();
            _appliedUpgrades.UnionWith(upgradesBefore);
            var settled = new List<SectionReport>();
            SettleAll(settled);
            problems.AddRange(settled.Select(report => report.ToString()));

            if (problems.Count == 0)
            {
                foreach (var section in _sections)
                    if (before.Sections.TryGetValue(section.Key, out var record)) _last[section.Key] = record;
                _log.Warning($"Save: a restore failed and everything was put back as it was. {failed.Problem}");
                return failed.WithRollback(true, null);
            }
            _log.Error($"Save: a restore failed, and putting it back failed too ({string.Join("; ", problems)}); those sections are saved with their last good data. {failed.Problem}");
            return failed.WithRollback(false, "putting it back failed: " + string.Join("; ", problems));
        }

        /** <summary>Applies <paramref name="prepared" /> if it was prepared; otherwise its preparation's report.</summary> */
        private SectionReport ApplyIfPrepared(PreparedSection prepared) => prepared.Succeeded ? TryApply(prepared) : prepared.Report;

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

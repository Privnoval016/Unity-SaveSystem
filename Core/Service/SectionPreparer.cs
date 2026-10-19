using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SaveSystem
{
    /** <summary>One section's data, read and ready to apply, or the reason it couldn't be.</summary> */
    internal readonly struct PreparedSection
    {
        #region Properties

        public ISaveSection Section { get; }
        public IPreparedRestore Restore { get; }
        public SectionReport Report { get; }
        public bool TooNew { get; }
        public bool Succeeded => Restore != null;

        #endregion

        #region Public API

        public PreparedSection(ISaveSection section, IPreparedRestore restore, SectionReport report, bool tooNew = false)
        {
            Section = section;
            Restore = restore;
            Report = report;
            TooNew = tooNew;
        }

        #endregion
    }

    /** <summary>Checks sections when they register, and turns a stored record into a restore: upgrade, then read.</summary> */
    internal static class SectionPreparer
    {
        #region Public API

        /** <summary>Throws when a section's key, version or upgrades are invalid.</summary> */
        public static void Validate(ISaveSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (!SaveNames.IsValidSectionKey(section.Key)) throw new ArgumentException($"Invalid section key '{section.Key}'.", nameof(section));
            if (section.Version < 1) throw new ArgumentException($"Section '{section.Key}' has version {section.Version}; versions start at 1.", nameof(section));

            var seen = new HashSet<int>();
            foreach (var upgrade in section.Upgrades ?? SectionUpgrade.None)
            {
                if (upgrade == null) throw new ArgumentException($"Section '{section.Key}' has a null upgrade.", nameof(section));
                if (upgrade.FromVersion >= section.Version)
                    throw new ArgumentException($"Section '{section.Key}' has an upgrade from version {upgrade.FromVersion}, not below its version {section.Version}.", nameof(section));
                if (!seen.Add(upgrade.FromVersion))
                    throw new ArgumentException($"Section '{section.Key}' has two upgrades from version {upgrade.FromVersion}.", nameof(section));
            }
        }

        /** <summary>Upgrades the record to the section's version and reads it, changing nothing live.</summary> */
        public static PreparedSection FromRecord(ISaveSection section, SectionRecord record, JsonSerializer serializer)
        {
            if (record.Version > section.Version)
                return Failed(section, record, $"stored version {record.Version} is newer than {section.Version}", true);

            var data = record.Data;
            for (var version = record.Version; version < section.Version; version++)
            {
                var upgrade = FindUpgrade(section, version);
                if (upgrade == null) return Failed(section, record, $"no upgrade from version {version}");
                try
                {
                    data = upgrade.Apply(data);
                }
                catch (Exception e)
                {
                    return Failed(section, record, $"upgrade from version {version} threw: {e.Message}");
                }
            }

            try
            {
                var outcome = record.Version == section.Version ? SectionOutcome.Restored : SectionOutcome.Upgraded;
                return new PreparedSection(section, section.Prepare(data, serializer), new SectionReport(section.Key, outcome, record.Version));
            }
            catch (Exception e)
            {
                return Failed(section, record, $"data could not be read: {e.Message}");
            }
        }

        /** <summary>Prepares a new game's state for the section.</summary> */
        public static PreparedSection Default(ISaveSection section)
        {
            try
            {
                return new PreparedSection(section, section.PrepareDefault(), new SectionReport(section.Key, SectionOutcome.Defaulted, SectionReport.NoVersion));
            }
            catch (Exception e)
            {
                return new PreparedSection(section, null, new SectionReport(section.Key, SectionOutcome.Failed, SectionReport.NoVersion, $"default could not be made: {e.Message}"));
            }
        }

        #endregion

        #region Helpers

        private static SectionUpgrade FindUpgrade(ISaveSection section, int fromVersion)
        {
            foreach (var upgrade in section.Upgrades ?? SectionUpgrade.None)
                if (upgrade.FromVersion == fromVersion) return upgrade;
            return null;
        }

        private static PreparedSection Failed(ISaveSection section, SectionRecord record, string problem, bool tooNew = false) =>
            new PreparedSection(section, null, new SectionReport(section.Key, SectionOutcome.Failed, record.Version, problem), tooNew);

        #endregion
    }
}

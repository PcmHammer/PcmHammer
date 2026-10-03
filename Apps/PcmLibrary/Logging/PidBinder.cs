// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking
{
    /// <summary>A parameter a package could be bound to.</summary>
    public class BindingCandidate
    {
        public BindingCandidate(string id, string name, string units)
        {
            this.Id = id;
            this.Name = name;
            this.Units = units;
        }

        public string Id { get; }

        public string Name { get; }

        public string Units { get; }

        public override string ToString() => this.Name;
    }

    /// <summary>How a binding was arrived at, so a questionable one can be reviewed.</summary>
    public enum BindingConfidence
    {
        None = 0,

        /// <summary>The gauge title matched a parameter name outright.</summary>
        Exact,

        /// <summary>Matched after stripping punctuation and noise words.</summary>
        Normalized,

        /// <summary>Matched through a known synonym, e.g. RPM to Engine Speed.</summary>
        Synonym,
    }

    public class BindingResult
    {
        public BindingResult(string title, string? pidId, BindingConfidence confidence)
        {
            this.Title = title;
            this.PidId = pidId;
            this.Confidence = confidence;
        }

        public string Title { get; }

        public string? PidId { get; }

        public BindingConfidence Confidence { get; }

        public bool IsBound => this.PidId != null;
    }

    /// <summary>
    /// Matches the titles in an imported package to parameters the logger can actually poll.
    /// </summary>
    /// <remarks>
    /// Matching is on names, never on the acquisition details in the source file. Those describe how
    /// some other interface read a packet, and taking them at face value is actively wrong: the
    /// widely circulated AVT LS1 definition labels PID 1155 "GM RPM" where the authoritative P01/P59
    /// list has that PID as the fuel level sensor. A name match can be checked by eye; a wrong PID
    /// looks right and reads nonsense.
    ///
    /// Names are compared after stripping punctuation and the words that vary between authors
    /// without changing meaning - "GM", "SAE", "SENSOR" - and then through a synonym table for the
    /// cases where two communities simply use different words for the same quantity. Nothing is
    /// guessed beyond that: a gauge with no confident match is left unbound and visibly so, which is
    /// more useful than a dashboard that looks complete and is subtly wrong.
    /// </remarks>
    public static class PidBinder
    {
        /// <summary>
        /// Words that appear in one naming convention and not another, and never distinguish two
        /// different quantities.
        /// </summary>
        private static readonly string[] NoiseWords =
        {
            "GM", "SAE", "OBD", "OBDII", "SENSOR", "VALUE", "SIGNAL", "ACTUAL", "CURRENT", "PCM",
        };

        /// <summary>
        /// Different words for the same quantity, as alternative spellings of one canonical form.
        /// Data rather than logic: extending it is adding a line, not changing how matching works.
        /// </summary>
        private static readonly string[][] Synonyms =
        {
            new[] { "RPM", "ENGINESPEED", "ENGINERPM", "ENGSPEED" },
            new[] { "CLT", "ECT", "COOLANT", "ENGINECOOLANT", "COOLANTTEMP", "ENGINECOOLANTTEMPERATURE" },
            new[] { "IAT", "INTAKEAIRTEMP", "INTAKEAIRTEMPERATURE", "MANIFOLDAIRTEMP", "MAT" },
            new[] { "MAP", "MANIFOLDABSOLUTEPRESSURE", "MAPKPA" },
            new[] { "MAF", "MASSAIRFLOW", "MAFGS", "MAFGPS" },
            new[] { "TPS", "TP", "THROTTLEPOSITION", "TPSVOLTS" },
            new[] { "KPH", "MPH", "VSS", "VEHICLESPEED", "ROADSPEED", "VEHICLESPEEDSENSOR" },
            new[] { "SPARKADV", "SPARKADVANCE", "IGNITIONTIMING", "TIMING", "ASPARK", "SPARK" },
            new[] { "KNOCKRETARD", "KNOCKRETARDDEGREES", "KNOCKDEG", "KR" },
            new[] { "STFTB1", "STFTBANK1", "SHORTTERMFTBANK1", "SHORTTERMFUELTRIMBANK1", "SHORTTERMFTLEFTBANK" },
            new[] { "STFTB2", "STFTBANK2", "SHORTTERMFTBANK2", "SHORTTERMFUELTRIMBANK2", "SHORTTERMFTRIGHTBANK" },
            new[] { "LTFTB1", "LTFTBANK1", "LONGTERMFTBANK1", "LONGTERMFUELTRIMBANK1", "LONGTERMFTLEFTBANK" },
            new[] { "LTFTB2", "LTFTBANK2", "LONGTERMFTBANK2", "LONGTERMFUELTRIMBANK2", "LONGTERMFTRIGHTBANK" },
            new[] { "O2BANK1", "O2LEFTFRONT", "O2B1", "O2LEFTUPSTREAM" },
            new[] { "O2BANK2", "O2RIGHTFRONT", "O2B2", "O2RIGHTUPSTREAM" },
            new[] { "AFRATIO", "AFR", "TARGETAFRATIO", "TARGETAFR", "AIRFUELRATIO" },
            new[] { "BATTVOLTS", "BATTERYVOLTAGE", "IGNITION1", "IGN1", "BATTERYVOLTS" },
            new[] { "DESIDLE", "DESIREDIDLE", "DESIREDIDLESPEED", "DESIREDIDLERPM" },
            new[] { "ENGINELOAD", "LOAD", "CALCULATEDLOAD", "CALCLOAD" },
            new[] { "INJECTORBPW", "BPW", "INJECTORPULSEWIDTH", "INJPW" },
        };

        private static readonly Dictionary<string, string> Canonical = BuildCanonicalMap();

        /// <summary>
        /// Choose a parameter for each title. Titles that do not match confidently come back unbound.
        /// </summary>
        public static IReadOnlyList<BindingResult> Bind(
            IEnumerable<string> titles,
            IEnumerable<BindingCandidate> candidates)
        {
            List<BindingCandidate> available = (candidates ?? Enumerable.Empty<BindingCandidate>()).ToList();

            Dictionary<string, BindingCandidate> byExactName = new Dictionary<string, BindingCandidate>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, BindingCandidate> byNormalized = new Dictionary<string, BindingCandidate>(StringComparer.Ordinal);
            Dictionary<string, BindingCandidate> byCanonical = new Dictionary<string, BindingCandidate>(StringComparer.Ordinal);

            foreach (BindingCandidate candidate in available)
            {
                if (!byExactName.ContainsKey(candidate.Name))
                {
                    byExactName[candidate.Name] = candidate;
                }

                string normalized = Normalize(candidate.Name);
                if (normalized.Length > 0 && !byNormalized.ContainsKey(normalized))
                {
                    byNormalized[normalized] = candidate;
                }

                string canonical = ToCanonical(normalized);
                if (canonical.Length > 0 && !byCanonical.ContainsKey(canonical))
                {
                    byCanonical[canonical] = candidate;
                }
            }

            List<BindingResult> results = new List<BindingResult>();
            foreach (string title in titles ?? Enumerable.Empty<string>())
            {
                string safeTitle = title ?? string.Empty;

                if (byExactName.TryGetValue(safeTitle, out BindingCandidate? exact))
                {
                    results.Add(new BindingResult(safeTitle, exact.Id, BindingConfidence.Exact));
                    continue;
                }

                string normalized = Normalize(safeTitle);
                if (normalized.Length > 0 && byNormalized.TryGetValue(normalized, out BindingCandidate? byNorm))
                {
                    results.Add(new BindingResult(safeTitle, byNorm.Id, BindingConfidence.Normalized));
                    continue;
                }

                string canonical = ToCanonical(normalized);
                if (canonical.Length > 0 && byCanonical.TryGetValue(canonical, out BindingCandidate? bySynonym))
                {
                    results.Add(new BindingResult(safeTitle, bySynonym.Id, BindingConfidence.Synonym));
                    continue;
                }

                results.Add(new BindingResult(safeTitle, null, BindingConfidence.None));
            }

            return results;
        }

        /// <summary>
        /// Bind every gauge and series in a package, leaving anything already bound alone. Returns
        /// how many were newly bound.
        /// </summary>
        public static int BindPackage(LoggerPackage package, IEnumerable<BindingCandidate> candidates)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            List<BindingCandidate> available = (candidates ?? Enumerable.Empty<BindingCandidate>()).ToList();

            List<string> titles = package.Dashboards.SelectMany(d => d.Gauges).Where(g => !g.IsBound).Select(g => g.Title)
                .Concat(package.Monitors.SelectMany(m => m.Series).Where(s => !s.IsBound).Select(s => s.Title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dictionary<string, BindingResult> decisions = Bind(titles, available)
                .GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int bound = 0;

            foreach (GaugeLayout gauge in package.Dashboards.SelectMany(d => d.Gauges).Where(g => !g.IsBound))
            {
                if (decisions.TryGetValue(gauge.Title, out BindingResult? result) && result.IsBound)
                {
                    gauge.PidId = result.PidId;
                    bound++;
                }
            }

            foreach (MonitorSeriesLayout series in package.Monitors.SelectMany(m => m.Series).Where(s => !s.IsBound))
            {
                if (decisions.TryGetValue(series.Title, out BindingResult? result) && result.IsBound)
                {
                    series.PidId = result.PidId;
                }
            }

            // Every bound id has to exist in the package's own PID list, or the display references
            // something that will never be logged.
            foreach (BindingCandidate candidate in available)
            {
                bool referenced = package.Dashboards.SelectMany(d => d.Gauges).Any(g => string.Equals(g.PidId, candidate.Id, StringComparison.OrdinalIgnoreCase))
                    || package.Monitors.SelectMany(m => m.Series).Any(s => string.Equals(s.PidId, candidate.Id, StringComparison.OrdinalIgnoreCase));

                if (referenced && package.FindPid(candidate.Id) == null)
                {
                    package.Pids.Add(new PackagedPid
                    {
                        Id = candidate.Id,
                        Name = candidate.Name,
                        Units = candidate.Units,
                    });
                }
            }

            return bound;
        }

        /// <summary>Upper case, letters and digits only, with the noise words removed.</summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char character in text.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            string collapsed = builder.ToString();
            foreach (string noise in NoiseWords)
            {
                // Only as a whole word at either end; "MAP" must not lose anything from "MAPSENSOR"
                // beyond the trailing SENSOR, and a noise word in the middle is left alone.
                if (collapsed.Length > noise.Length && collapsed.StartsWith(noise, StringComparison.Ordinal))
                {
                    collapsed = collapsed.Substring(noise.Length);
                }

                if (collapsed.Length > noise.Length && collapsed.EndsWith(noise, StringComparison.Ordinal))
                {
                    collapsed = collapsed.Substring(0, collapsed.Length - noise.Length);
                }
            }

            return collapsed;
        }

        private static string ToCanonical(string normalized)
        {
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            return Canonical.TryGetValue(normalized, out string? canonical) ? canonical : normalized;
        }

        private static Dictionary<string, string> BuildCanonicalMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string[] group in Synonyms)
            {
                string canonical = group[0];
                foreach (string spelling in group)
                {
                    map[spelling] = canonical;
                }
            }

            return map;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace PcmHacking
{
    /// <summary>
    /// The .plz format: a ZIP holding manifest.json, which describes the PIDs to log and every way
    /// of displaying them. See Docs/pcmlogger-file-format.md.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape as .phz - a ZIP, a JSON manifest, a readme for anyone who opens
    /// it with an archiver - so the two formats are learned once. A ZIP rather than a bare JSON file
    /// because the format is expected to grow: a dashboard background image, a captured log to
    /// replay, exported histogram data. Those want to be files beside the manifest, and retrofitting
    /// a container later would mean two formats to read.
    /// </remarks>
    public static class PlzFormat
    {
        public const string Extension = ".plz";

        private const string ManifestEntryName = "manifest.json";

        private const string ReadmeEntryName = "readme.txt";

        public static LoggerPackage Load(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                return Load(stream);
            }
        }

        public static LoggerPackage Load(Stream stream)
        {
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException exception)
            {
                // Otherwise the user is shown ZIP's internal complaint about a central directory,
                // which says nothing about what they actually opened.
                throw new PackageException("This is not a PcmLogger package - it is not a readable archive.", exception);
            }

            using (archive)
            {
                ZipArchiveEntry manifest = archive.GetEntry(ManifestEntryName)
                    ?? throw new PackageException("This is not a PcmLogger package - it has no manifest.json.");

                string json;
                using (StreamReader reader = new StreamReader(manifest.Open(), Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                LoggerPackage package = JsonConvert.DeserializeObject<LoggerPackage>(json)
                    ?? throw new PackageException("The package manifest is empty or unreadable.");

                // A newer file may contain sections this build knows nothing about. Reading it
                // anyway would silently drop them on the next save, so refuse rather than quietly
                // discard someone's work.
                if (package.FormatVersion > LoggerPackage.CurrentFormatVersion)
                {
                    throw new PackageException(string.Format(
                        "This package is version {0}, and this version of PcmLogger understands up to {1}. Update PcmLogger to open it.",
                        package.FormatVersion,
                        LoggerPackage.CurrentFormatVersion));
                }

                return package;
            }
        }

        public static void Save(string path, LoggerPackage package)
        {
            using (FileStream stream = File.Create(path))
            {
                Save(stream, package);
            }
        }

        public static void Save(Stream stream, LoggerPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                string json = JsonConvert.SerializeObject(package, Formatting.Indented);
                WriteTextEntry(archive, ManifestEntryName, json);
                WriteTextEntry(archive, ReadmeEntryName, BuildReadme(package));
            }
        }

        private static void WriteTextEntry(ZipArchive archive, string name, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        private static string BuildReadme(LoggerPackage package)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("PcmLogger dashboard package (.plz)");
            text.AppendLine("==================================");
            text.AppendLine();
            text.AppendLine("This is a ZIP. manifest.json lists the PIDs to log and the dashboards,");
            text.AppendLine("monitors and histograms that display them.");
            text.AppendLine("Open it with PcmLogger rather than extracting it by hand.");
            text.AppendLine();

            if (!string.IsNullOrEmpty(package.Description))
            {
                text.AppendLine("Description: " + package.Description);
            }

            if (!string.IsNullOrEmpty(package.Source.Name))
            {
                text.AppendLine("Imported from: " + package.Source.Name);
            }

            if (!string.IsNullOrEmpty(package.Communications.Protocol))
            {
                text.AppendLine("Protocol: " + package.Communications.Protocol);
            }

            text.AppendLine(string.Format(
                "Contents: {0} PIDs, {1} dashboards ({2} gauges), {3} monitors, {4} histograms.",
                package.Pids.Count,
                package.Dashboards.Count,
                package.Dashboards.Sum(d => d.Gauges.Count),
                package.Monitors.Count,
                package.Histograms.Count));

            int unbound = package.UnboundGaugeCount;
            if (unbound > 0)
            {
                text.AppendLine(unbound + " gauge(s) are not bound to a PID and will show no value.");
            }

            return text.ToString();
        }
    }
}

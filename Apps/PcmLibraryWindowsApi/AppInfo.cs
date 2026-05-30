// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Reflection;

namespace PcmHacking
{
    public static class AppInfo
    {
        public const string CopyrightNotice = "Copyright (C) 2018-2026 PcmHacking.net - GPL v3";

        /// <summary>
        /// Returns "{appName}\n{Version/Build line}" for display in logs.
        /// For a compact single-line form (e.g. window titles) replace '\n' with ' '.
        /// </summary>
        public static string GetNameAndVersion(string appName, long buildTimeTicks)
        {
            return $"{appName}\n{GetVersionOrBuildLine(buildTimeTicks)}";
        }

        /// <summary>
        /// Returns "Version: x.x.x" for release builds or "Build: date time" for dev builds.
        /// </summary>
        public static string GetVersionOrBuildLine(long buildTimeTicks)
        {
            string? version = GetReleaseVersion();
            if (version != null)
                return $"Version: {version}";

            DateTime localTime = new DateTime(buildTimeTicks).ToLocalTime();
            return $"Build: {localTime.ToShortDateString()} {localTime.ToShortTimeString()}";
        }

        /// <summary>
        /// Returns "Version: x.x.x" for release builds, or null for dev builds where no version is set.
        /// Use this when no build timestamp is available (e.g. Uno multi-target builds).
        /// </summary>
        public static string? GetVersionLine()
        {
            string? version = GetReleaseVersion();
            return version != null ? $"Version: {version}" : null;
        }

        /// <summary>
        /// Returns "Running at: {day}, {date}, {time} {UTC offset}" using the local timezone.
        /// </summary>
        public static string GetRunningAtMessage()
        {
            return "Running at: " + DateTime.Now.ToString("dddd, MMMM dd yyyy, HH:mm:ss");
        }

        /// <summary>
        /// Returns the version string from the entry assembly, or null when running
        /// from an unversioned (development) build.
        /// AssemblyFileVersion is used to detect whether a release version is set;
        /// AssemblyInformationalVersion is used for display (may include a -Preview suffix).
        /// </summary>
        private static string? GetReleaseVersion()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly();
                if (asm == null) return null;
                string location = asm.Location;
                if (string.IsNullOrEmpty(location)) return null;
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(location);
                string fileVersion = fvi.FileVersion;
                if (string.IsNullOrEmpty(fileVersion) || fileVersion == "0.0.0.0")
                    return null;
                // Prefer InformationalVersion for display; it may include a -Preview suffix.
                string infoVersion = fvi.ProductVersion;
                return !string.IsNullOrEmpty(infoVersion) ? infoVersion : fileVersion;
            }
            catch { return null; }
        }
    }
}

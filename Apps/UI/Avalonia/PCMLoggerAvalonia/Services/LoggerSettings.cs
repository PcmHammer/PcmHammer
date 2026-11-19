using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PCMLoggerAvalonia.Services;

/// <summary>
/// The logger's saved settings (for now just the recent-profiles list), kept as JSON in the user's
/// application-data folder, as the flasher does for its settings. Reading or writing is best effort:
/// a missing or unreadable file means defaults, and a failed save is ignored, so settings can never
/// stop the logger from starting or working.
/// </summary>
public class LoggerSettings
{
    public const int MaxRecentProfiles = 10;

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCMLogger");

    private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "settings.json");

    /// <summary>Full paths of recently opened or saved profiles, newest first.</summary>
    public List<string> RecentProfiles { get; set; } = new();

    // Auto-save: record only while the chosen condition holds. Which of them are ticked is remembered.
    public bool AutoSaveAccelerator { get; set; }
    public bool AutoSaveKnockRetard { get; set; }
    public bool AutoSaveCruiseButton { get; set; }

    public static LoggerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<LoggerSettings>(json) ?? new LoggerSettings();
            }
        }
        catch
        {
            // Fall back to defaults on a read error.
        }

        return new LoggerSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Nothing useful to do; the list just won't survive a restart.
        }
    }

    /// <summary>Move a profile to the top of the recent list, dropping duplicates and the oldest entries.</summary>
    public void AddRecentProfile(string path)
    {
        this.RecentProfiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        this.RecentProfiles.Insert(0, path);
        if (this.RecentProfiles.Count > MaxRecentProfiles)
        {
            this.RecentProfiles.RemoveRange(MaxRecentProfiles, this.RecentProfiles.Count - MaxRecentProfiles);
        }
    }

    public void RemoveRecentProfile(string path)
    {
        this.RecentProfiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
    }
}

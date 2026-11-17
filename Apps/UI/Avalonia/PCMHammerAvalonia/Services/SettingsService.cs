using System.Text.Json;
using PCMHammerAvalonia.Models;

namespace PCMHammerAvalonia.Services;

public class SettingsService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCMHammer");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    public AppSettings Settings { get; private set; }

    public SettingsService()
    {
        Settings = LoadSettings();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Fallback to defaults on read error
        }

        return new AppSettings();
    }

    public void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Handle save error / write log
        }
    }
}
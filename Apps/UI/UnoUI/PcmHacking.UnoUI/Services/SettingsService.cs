using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json.Linq;

namespace PcmHacking.UnoUI.Services;

public class SettingsChangedMessage { }

public interface ISettingsService
{
    string GetObd2DeviceCategory();
    string GetJ2534DeviceName();
    string GetObd2SerialPortName();
    string GetObd2SerialDeviceName();
    bool IsCanEnabled();
    string GetCanSerialPortName();

    CurrentSettings LoadConnectionSettings();
    void SaveConnectionSettings(CurrentSettings settings);

    IEnumerable<string> GetMruLogProfiles();    
    void AddMruLogProfile(string path);

    // Directory for storing profiles (to initialize file-open dialog)
    string GetMruLogProfilePath();
    void SetMruLogProfilePath(string path);

    string GetDataLogFolder();
    void SetDataLogFolder(string folder);

    bool Is4xReadWriteEnabled();
    void Is4xReadWriteEnabled(bool enabled);

    bool IsCalibrationWritePreferred();
    void ShouldPreferCalibrationWrite(bool preferCalibrationWrite);

    string GetLastWrittenFile();
    void SetLastWrittenFile(string path);
}

public class SettingsService : ISettingsService
{
    private const string Obd2DeviceCategoryKey = "Obd2DeviceCategory";
    private const string J2534DeviceNameKey = "J2534DeviceName";
    private const string Obd2SerialPortNameKey = "Obd2SerialPortName";
    private const string Obd2SerialDeviceNameKey = "Obd2SerialDeviceName";
    private const string CanEnabledKey = "CanEnabled";
    private const string CanSerialPortNameKey = "CanSerialPortName";
    private const string LogMruProfilesKey = "LogMruProfiles";
    private const string LogMruProfilePathKey = "LogMruProfilePath";
    private const string DataLogFolderKey = "DataLogFolder";
    private const string Is4xReadWriteEnabledKey = "Is4xReadWriteEnabled";
    private const string PreferCalibrationWriteKey = "PreferCalibrationWrite";
    private const string LastWrittenFileKey = "LastWrittenFile";

    public SettingsService()
    {
    }

    //#if WINDOWS10_0_26100_0_OR_GREATER
    //    Microsoft.Storage.ApplicationData.ApplicationDataContainer? localSettings;
    //#else
    // This is supported on most platforms, but not for Unpackaged Windows apps,
    // because it depends on an app-data folder that only exists for packaged apps.
    ApplicationDataContainer? localSettings;
//#endif

    private ApplicationDataContainer LocalSettings
    {
        get
        {
            if (localSettings == null)
            {
#if DESKTOP1_0_OR_GREATER || WINAPPSDK_PACKAGED || MACCATALYST || IOS || ANDROID
                localSettings = ApplicationData.Current.LocalSettings;
#elif WINDOWS && !WINAPPSDK_PACKAGED
                localSettings = ApplicationData.GetForUnpackaged("PcmHacking.net", "PCM Hammer");
#else
                try
                {
                    // Users shouldn't encounter this, but it's confusing when this happens in the debugger.
                    localSettings = ApplicationData.Current.LocalSettings;
                }
                catch (Exception ex)
                {
                    // TODO: there's probably a better way to log this.
                    Console.WriteLine("Unable to load application settings.");
                    Console.WriteLine(ex.ToString());
                    throw;
                }
#endif
            }
            return localSettings;
        }
    }

    public string GetObd2DeviceCategory()
    {
        return LocalSettings.Values[Obd2DeviceCategoryKey] as string ?? string.Empty;
    }

    public string GetObd2SerialPortName()
    {
        return LocalSettings.Values[Obd2SerialPortNameKey] as string ?? string.Empty;
    }

    public string GetObd2SerialDeviceName()
    {
        return LocalSettings.Values[Obd2SerialDeviceNameKey] as string ?? string.Empty;
    }

    public string GetJ2534DeviceName()
    {
        return LocalSettings.Values[J2534DeviceNameKey] as string ?? string.Empty;
    }

    public bool IsCanEnabled()
    {
        return LocalSettings.Values[CanEnabledKey] as string == "true";
    }

    public string GetCanSerialPortName()
    {
        return LocalSettings.Values[CanSerialPortNameKey] as string ?? string.Empty;
    }

    public CurrentSettings LoadConnectionSettings()
    {
        return new CurrentSettings(
            LocalSettings.Values[Obd2DeviceCategoryKey] as string ?? string.Empty,
            LocalSettings.Values[Obd2SerialPortNameKey] as string ?? string.Empty,
            LocalSettings.Values[Obd2SerialDeviceNameKey] as string ?? string.Empty,
            LocalSettings.Values[J2534DeviceNameKey] as string ?? string.Empty,
            LocalSettings.Values[CanEnabledKey] as string == "true",
            LocalSettings.Values[CanSerialPortNameKey] as string ?? string.Empty);
    }

    public void SaveConnectionSettings(CurrentSettings settings)
    {
        LocalSettings.Values[J2534DeviceNameKey] = settings.J2534DeviceName;
        LocalSettings.Values[Obd2DeviceCategoryKey] = settings.DeviceCategory;
        LocalSettings.Values[Obd2SerialPortNameKey] = settings.Obd2SerialPortName;
        LocalSettings.Values[Obd2SerialDeviceNameKey] = settings.Obd2SerialDeviceName;
        LocalSettings.Values[CanEnabledKey] = settings.CanEnabled ? "true" : "false";
        LocalSettings.Values[CanSerialPortNameKey] = settings.CanPort;
    }

    public IEnumerable<string> GetMruLogProfiles()
    {
        string json = LocalSettings.Values[LogMruProfilesKey] as string ?? string.Empty;
        if (string.IsNullOrEmpty(json))
        {
            return new string[0];
        }

        JObject? jsonObject = JObject.Parse(json);
        if (jsonObject == null)
        {
            return new string[0];
        }

        JObject j = JObject.Parse(json);
        List<string> result = new List<string>();
        foreach (var item in j)
        {
            string? path = item.Value?.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                result.Add(path);
            }
        }
        return result;
    }

    public void AddMruLogProfile(string path)
    {
        var list = new List<string>(this.GetMruLogProfiles());
        if (list.Contains(path))
        {
            list.Remove(path);
        }

        list.Insert(0, path);

        if (list.Count() > 10)
        {
            list.RemoveAt(10);
        }

        JObject j = new JObject();
        foreach (var item in list)
        {
            j.Add(item, item);
        }

        LocalSettings.Values[LogMruProfilesKey] = j.ToString();
    }

    // Not used - the OpenFilePicker doesn't support it
    public string GetMruLogProfilePath()
    {
        string? path = LocalSettings.Values[LogMruProfilePathKey] as string ?? string.Empty;
        return path;
    }

    // Not used - the OpenFilePicker doesn't support it
    public void SetMruLogProfilePath(string path)
    {
        LocalSettings.Values[LogMruProfilePathKey] = path;
    }


    public string GetDataLogFolder()
    {
        string? folder = LocalSettings.Values[DataLogFolderKey] as string;
        if (string.IsNullOrEmpty(folder))
        {
            return "[no location configured]";
        }

        return LocalSettings.Values[DataLogFolderKey] as string ?? string.Empty;
    }
    public void SetDataLogFolder(string folder)
    {
        LocalSettings.Values[DataLogFolderKey] = folder;
    }

    public bool Is4xReadWriteEnabled()
    {
        return (bool)(LocalSettings.Values[Is4xReadWriteEnabledKey] ?? true);
    }
    public void Is4xReadWriteEnabled(bool enabled)
    {
        LocalSettings.Values[Is4xReadWriteEnabledKey] = enabled;
    }

    public bool IsCalibrationWritePreferred()
    {
        return (bool)(LocalSettings.Values[PreferCalibrationWriteKey] ?? false);
    }

    public void ShouldPreferCalibrationWrite(bool preferCalibrationWrite)
    {
        LocalSettings.Values[PreferCalibrationWriteKey] = preferCalibrationWrite;
    }

    public string GetLastWrittenFile()
    {
        return LocalSettings.Values[LastWrittenFileKey] as string ?? string.Empty;
    }

    public void SetLastWrittenFile(string path)
    {
        LocalSettings.Values[LastWrittenFileKey] = path;
    }
}

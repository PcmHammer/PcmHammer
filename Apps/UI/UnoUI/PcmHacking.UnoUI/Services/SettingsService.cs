// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json.Linq;
using Windows.Foundation.Collections;

namespace PcmHacking.UnoUI.Services;

public class SettingsChangedMessage { }

public interface ISettingsService
{
    string GetObd2DeviceCategory();
    string GetJ2534DeviceName();
    SerialPortListing GetBluetoothDeviceAddress();
    SerialPortListing GetObd2SerialPortName();
    string GetObd2SerialDeviceName();
    bool IsCanEnabled();
    SerialPortListing GetCanSerialPortName();

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

    bool IsDebugMode();
    void SetDebugMode(bool enabled);

    bool IsCalibrationWritePreferred();
    void ShouldPreferCalibrationWrite(bool preferCalibrationWrite);

    string GetLastWrittenFile();
    void SetLastWrittenFile(string path);

    string GetCustomKey();
    void SetCustomKey(string value);
    bool GetUseCustomKey();
    void SetUseCustomKey(bool value);

    bool GetUseAcceleratorToSaveLogs();
    void SetUseAcceleratorToSaveLogs(bool value);

    bool GetUseCruiseButtonToSaveLogs();
    void SetUseCruiseButtonToSaveLogs(bool value);

    bool GetUseKnockRetardToSaveLogs();
    void SetUseKnockRetardToSaveLogs(bool value);
}

public class SettingsService : ISettingsService
{
    private const string Obd2DeviceCategoryKey = "Obd2DeviceCategory";
    private const string J2534DeviceNameKey = "J2534DeviceName";
    private const string Obd2SerialPortNameKey = "Obd2SerialPortName";
    private const string Obd2SerialDeviceNameKey = "Obd2SerialDeviceName";
    private const string BluetoothDeviceNameKey = "BluetoothDeviceName";
    private const string CanEnabledKey = "CanEnabled";
    private const string CanSerialPortNameKey = "CanSerialPortName";
    private const string LogMruProfilesKey = "LogMruProfiles";
    private const string LogMruProfilePathKey = "LogMruProfilePath";
    private const string DataLogFolderKey = "DataLogFolder";
    private const string Is4xReadWriteEnabledKey = "Is4xReadWriteEnabled";
    private const string PreferCalibrationWriteKey = "PreferCalibrationWrite";
    private const string LastWrittenFileKey = "LastWrittenFile";
    private const string CustomKeyKey = "CustomKey";
    private const string UseCustomKeyKey = "UseCustomKey";
    private const string UseAcceleratorToSaveLogsKey = "UseAcceleratorToSaveLogs";
    private const string UseCruiseButtonToSaveLogsKey = "UseCruiseButtonToSaveLogs";
    private const string UseKnockRetardToSaveLogsKey = "UseKnockRetardToSaveLogs";
    private const string IsDebugModeKey = "IsDebugMode";



    //#if WINDOWS10_0_26100_0_OR_GREATER
    //    Microsoft.Storage.ApplicationData.ApplicationDataContainer? localSettings;
    //#else
    // This is supported on most platforms, but not for Unpackaged Windows apps,
    // because it depends on an app-data folder that only exists for packaged apps.
    // 
    // This mock interface will allow us to switch where the data originates from,
    // giving us the ability to use this service as is for most platforms while 
    // freely allowing a custom option for Unpackaged windows.
    private IPropertySet _settingsListInterface
    {
        get
        {
#if DESKTOP1_0_OR_GREATER || WINAPPSDK_PACKAGED || MACCATALYST || IOS || ANDROID
            return ApplicationData.Current.LocalSettings.Values;
#elif WINDOWS && !WINAPPSDK_PACKAGED            
            if (_unpackagedSettingsStore == null)
            {
                _unpackagedSettingsStore = AutoSaveDictionary.Load();
            }
            // TODO: Introduce a local file path and name, and try to load data into this array.
            return (IPropertySet)_unpackagedSettingsStore;
#else
            try
            {
                // Users shouldn't encounter this, but it's confusing when this happens in the debugger.
                return ApplicationData.Current.LocalSettings.Values;
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
    }

    private AutoSaveDictionary _unpackagedSettingsStore { get; set; } = null!;


    public SettingsService()
    {
    }


    public string GetObd2DeviceCategory()
    {
        return _settingsListInterface[Obd2DeviceCategoryKey] as string ?? string.Empty;
    }

    public SerialPortListing GetObd2SerialPortName()
    {
        string? result = _settingsListInterface[Obd2SerialPortNameKey] as string ?? string.Empty;
        return new SerialPortListing
        {
            PortName = result
        };
    }

    public string GetObd2SerialDeviceName()
    {
        return _settingsListInterface[Obd2SerialDeviceNameKey] as string ?? string.Empty;
    }

    public SerialPortListing GetBluetoothDeviceAddress()
    {
        string result = _settingsListInterface[BluetoothDeviceNameKey] as string ?? string.Empty;
        return new SerialPortListing
        {
            PortName = result
        };
    }

    public string GetJ2534DeviceName()
    {
        return _settingsListInterface[J2534DeviceNameKey] as string ?? string.Empty;
    }

    public bool IsCanEnabled()
    {
        return _settingsListInterface[CanEnabledKey] as string == "true";
    }

    public SerialPortListing GetCanSerialPortName()
    {
        string? result = _settingsListInterface[CanSerialPortNameKey] as string ?? string.Empty;

        return new SerialPortListing
        {
            PortName = result
        };
    }

    public CurrentSettings LoadConnectionSettings()
    {
        string deviceCategory = _settingsListInterface[Obd2DeviceCategoryKey] as string ?? string.Empty;
        string nameOrPort = string.Empty;
        if (!string.IsNullOrEmpty(deviceCategory))
        {
            nameOrPort =
                deviceCategory == DeviceConstants.DeviceCategorySerial ? _settingsListInterface[Obd2SerialPortNameKey] as string ?? string.Empty :
                deviceCategory == DeviceConstants.DeviceCategoryJ2534 ? _settingsListInterface[J2534DeviceNameKey] as string ?? string.Empty :
                deviceCategory == DeviceConstants.DeviceCategoryBT ? _settingsListInterface[BluetoothDeviceNameKey] as string ?? string.Empty : "";
        }
        return new CurrentSettings(
            deviceCategory,
            nameOrPort,
            _settingsListInterface[CanEnabledKey] as string == "true",
            _settingsListInterface[CanSerialPortNameKey] as string ?? string.Empty);
    }

    public void SaveConnectionSettings(CurrentSettings settings)
    {
        _settingsListInterface[Obd2DeviceCategoryKey] = settings.DeviceCategory;
        switch (settings.DeviceCategory)
        {
            case DeviceConstants.DeviceCategorySerial:
                _settingsListInterface[Obd2SerialPortNameKey] = settings.DeviceNameOrPort;
                break;
            case DeviceConstants.DeviceCategoryJ2534:
                _settingsListInterface[J2534DeviceNameKey] = settings.DeviceNameOrPort;
                break;
            case DeviceConstants.DeviceCategoryBT:
                _settingsListInterface[BluetoothDeviceNameKey] = settings.DeviceNameOrPort;
                break;
        }
        _settingsListInterface[CanEnabledKey] = settings.CanEnabled ? "true" : "false";
        _settingsListInterface[CanSerialPortNameKey] = settings.CanPort;
    }

    public IEnumerable<string> GetMruLogProfiles()
    {
        string json = _settingsListInterface[LogMruProfilesKey] as string ?? string.Empty;
        if (string.IsNullOrEmpty(json))
        {
            return new string[0];
        }

        JObject? jsonObject = JObject.Parse(json);
        if (jsonObject == null)
        {
            return new string[0];
        }
        List<string> result = new List<string>();
        foreach (var item in jsonObject)
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

        _settingsListInterface[LogMruProfilesKey] = j.ToString();
    }

    // Not used - the OpenFilePicker doesn't support it
    public string GetMruLogProfilePath()
    {
        string? path = _settingsListInterface[LogMruProfilePathKey] as string ?? string.Empty;
        return path;
    }

    // Not used - the OpenFilePicker doesn't support it
    public void SetMruLogProfilePath(string path)
    {
        _settingsListInterface[LogMruProfilePathKey] = path;
    }


    public string GetDataLogFolder()
    {
        string? folder = _settingsListInterface[DataLogFolderKey] as string;
        if (string.IsNullOrEmpty(folder))
        {
            return "[no location configured]";
        }

        return _settingsListInterface[DataLogFolderKey] as string ?? string.Empty;
    }
    public void SetDataLogFolder(string folder)
    {
        _settingsListInterface[DataLogFolderKey] = folder;
    }

    public bool Is4xReadWriteEnabled()
    {
        return (bool)(_settingsListInterface[Is4xReadWriteEnabledKey] ?? true);
    }
    public void Is4xReadWriteEnabled(bool enabled)
    {
        _settingsListInterface[Is4xReadWriteEnabledKey] = enabled;
    }

    public bool IsCalibrationWritePreferred()
    {
        return (bool)(_settingsListInterface[PreferCalibrationWriteKey] ?? false);
    }

    public void ShouldPreferCalibrationWrite(bool preferCalibrationWrite)
    {
        _settingsListInterface[PreferCalibrationWriteKey] = preferCalibrationWrite;
    }

    public string GetLastWrittenFile()
    {
        return _settingsListInterface[LastWrittenFileKey] as string ?? string.Empty;
    }

    public void SetLastWrittenFile(string path)
    {
        _settingsListInterface[LastWrittenFileKey] = path;
    }

    public string GetCustomKey()
    {
        return _settingsListInterface[CustomKeyKey] as string ?? string.Empty;
    }

    public void SetCustomKey(string value)
    {
        _settingsListInterface[CustomKeyKey] = value;
    }

    public bool GetUseCustomKey()
    {
        return (bool)(_settingsListInterface[UseCustomKeyKey] ?? false);
    }

    public void SetUseCustomKey(bool value)
    {
        _settingsListInterface[UseCustomKeyKey] = value;
    }

    public bool GetUseAcceleratorToSaveLogs()
    {
        return (bool)(_settingsListInterface[UseAcceleratorToSaveLogsKey] ?? false);
    }

    public void SetUseAcceleratorToSaveLogs(bool value)
    {
        _settingsListInterface[UseAcceleratorToSaveLogsKey] = value;
    }

    public bool GetUseCruiseButtonToSaveLogs()
    {
        return (bool)(_settingsListInterface[UseCruiseButtonToSaveLogsKey] ?? false);
    }

    public void SetUseCruiseButtonToSaveLogs(bool value)
    {
        _settingsListInterface[UseCruiseButtonToSaveLogsKey] = value;
    }


    public bool GetUseKnockRetardToSaveLogs()
    {
        return (bool)(_settingsListInterface[UseKnockRetardToSaveLogsKey] ?? false);
    }

    public void SetUseKnockRetardToSaveLogs(bool value)
    {
        _settingsListInterface[UseKnockRetardToSaveLogsKey] = value;
    }

    public bool IsDebugMode()
    {
        return (bool)(_settingsListInterface[IsDebugModeKey] ?? false);
    }

    public void SetDebugMode(bool enabled)
    {
        _settingsListInterface[IsDebugModeKey] = enabled;
    }
}

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
    ValueTask<string> GetObd2DeviceCategory(CancellationToken ct);
    ValueTask<string> GetJ2534DeviceName(CancellationToken ct);
    ValueTask<string> GetObd2SerialPortName(CancellationToken ct);
    ValueTask<string> GetObd2SerialDeviceName(CancellationToken ct);
    ValueTask<bool> IsCanEnabled(CancellationToken ct);
    ValueTask<string> GetCanSerialPortName(CancellationToken ct);

    CurrentSettings LoadConnectionSettings();
    void SaveConnectionSettings(CurrentSettings settings);

    ValueTask<string> GetDataLogFolder(CancellationToken ct);
    void SetDataLogFolder(string folder);
}

public class SettingsService : ISettingsService
{
    private const string Obd2DeviceCategoryKey = "Obd2DeviceCategory";
    private const string J2534DeviceNameKey = "J2534DeviceName";
    private const string Obd2SerialPortNameKey = "Obd2SerialPortName";
    private const string Obd2SerialDeviceNameKey = "Obd2SerialDeviceName";
    private const string CanEnabledKey = "CanEnabled";
    private const string CanSerialPortNameKey = "CanSerialPortName";
    private const string DataLogFolderKey = "DataLogFolder";

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
                try
                {
                    // This throws an exception when invoked from an unpackaged Windows app, because packaging
                    // is how the app gets the app data folder to store settings in. 
                    // Users shouldn't encounter this, but it's confusing when this happens in the debugger.
                    localSettings = ApplicationData.Current.LocalSettings;
                }
                catch (Exception ex)
                {
                    // TODO: there's probably a better way to log this.
                    Console.WriteLine("Unable to load application settings. Is this an unpackaged Windows app?");
                    Console.WriteLine(ex.ToString());
                    throw;
                }
            }
            return localSettings;
        }
    }

    public ValueTask<string> GetObd2DeviceCategory(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[Obd2DeviceCategoryKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetObd2SerialPortName(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[Obd2SerialPortNameKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetObd2SerialDeviceName(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[Obd2SerialDeviceNameKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetJ2534DeviceName(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[J2534DeviceNameKey] as string ?? string.Empty);
    }

    public ValueTask<bool> IsCanEnabled(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[CanEnabledKey] as string == "true");
    }

    public ValueTask<string> GetCanSerialPortName(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[CanSerialPortNameKey] as string ?? string.Empty);
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

    public ValueTask<string> GetDataLogFolder(CancellationToken ct)
    {
        string? folder = LocalSettings.Values[DataLogFolderKey] as string;
        if (string.IsNullOrEmpty(folder))
        {
            return ValueTask.FromResult("[no location configured]");
        }

        return ValueTask.FromResult(LocalSettings.Values[DataLogFolderKey] as string ?? string.Empty);
    }
    public void SetDataLogFolder(string folder)
    {
        LocalSettings.Values[DataLogFolderKey] = folder;
    }
}

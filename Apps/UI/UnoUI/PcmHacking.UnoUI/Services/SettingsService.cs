using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PcmHacking.UnoUI.Services;

public interface ISettingsService
{
    ValueTask<string> GetObd2DeviceCategory(CancellationToken ct);
    ValueTask<string> GetObd2SerialPortName(CancellationToken ct);
    ValueTask<string> GetObd2SerialDeviceType(CancellationToken ct);
    ValueTask<bool> IsCanEnabled(CancellationToken ct);
    ValueTask<string> GetCanSerialPortName(CancellationToken ct);
    ValueTask<string> GetDataLogFolder(CancellationToken ct);

    void SettingsChanged(CurrentSettings settings);

    void DataLogFolderChanged(string folder);
}

internal class SettingsService : ISettingsService
{
    private const string Obd2DeviceCategoryKey = "Obd2DeviceCategory";
    private const string Obd2SerialPortNameKey = "Obd2SerialPortName";
    private const string Obd2SerialDeviceTypeKey = "Obd2SerialDeviceType";
    private const string CanEnabledKey = "CanEnabled";
    private const string CanSerialPortNameKey = "CanSerialPortName";
    private const string DataLogFolderKey = "DataLogFolder";

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
                localSettings = ApplicationData.Current.LocalSettings;
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

    public ValueTask<string> GetObd2SerialDeviceType(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[Obd2SerialDeviceTypeKey] as string ?? string.Empty);
    }

    public ValueTask<bool> IsCanEnabled(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[CanEnabledKey] as string == "true");
    }

    public ValueTask<string> GetCanSerialPortName(CancellationToken ct)
    {
        return ValueTask.FromResult(LocalSettings.Values[CanSerialPortNameKey] as string ?? string.Empty);
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

    public void SettingsChanged(CurrentSettings settings)
    {
        LocalSettings.Values[Obd2DeviceCategoryKey] = settings.DeviceCategory;
        LocalSettings.Values[Obd2SerialPortNameKey] = settings.Obd2SerialPortName;
        LocalSettings.Values[Obd2SerialDeviceTypeKey] = settings.Obd2SerialDeviceType;
        LocalSettings.Values[CanEnabledKey] = settings.CanEnabled ? "true" : "false";
        LocalSettings.Values[CanSerialPortNameKey] = settings.CanPort;
    }

    public void DataLogFolderChanged(string folder)
    {
        LocalSettings.Values[DataLogFolderKey] = folder;
    }
}

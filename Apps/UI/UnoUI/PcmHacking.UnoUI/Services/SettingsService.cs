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
    ValueTask<string> GetCanSerialPortName(CancellationToken ct);
    
    void SettingsChanged(CurrentSettings settings);
}

internal class SettingsService : ISettingsService
{
    private const string Obd2DeviceCategoryKey = "Obd2DeviceCategory";
    private const string Obd2SerialPortNameKey = "Obd2SerialPortName";
    private const string Obd2SerialDeviceTypeKey = "Obd2SerialDeviceType";
    private const string CanSerialPortNameKey = "CanSerialPortName";

    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

    public ValueTask<string> GetObd2DeviceCategory(CancellationToken ct)
    {
        return ValueTask.FromResult(localSettings.Values[Obd2DeviceCategoryKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetObd2SerialPortName(CancellationToken ct)
    {
        return ValueTask.FromResult(localSettings.Values[Obd2SerialPortNameKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetObd2SerialDeviceType(CancellationToken ct)
    {
        return ValueTask.FromResult(localSettings.Values[Obd2SerialDeviceTypeKey] as string ?? string.Empty);
    }

    public ValueTask<string> GetCanSerialPortName(CancellationToken ct)
    {
        return ValueTask.FromResult(localSettings.Values[CanSerialPortNameKey] as string ?? string.Empty);
    }

    public void SettingsChanged(CurrentSettings settings)
    {
        localSettings.Values[Obd2DeviceCategoryKey] = settings.DeviceCategory;
        localSettings.Values[Obd2SerialPortNameKey] = settings.Obd2SerialPortName;
        localSettings.Values[Obd2SerialDeviceTypeKey] = settings.Obd2SerialDeviceType;
        localSettings.Values[CanSerialPortNameKey] = settings.CanPort;
    }
}

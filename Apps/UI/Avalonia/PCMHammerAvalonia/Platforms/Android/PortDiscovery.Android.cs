// SPDX-License-Identifier: GPL-3.0-only
// The Android stand-in for the Windows PortDiscovery/SerialPortInfo (which live in PcmLibraryWindowsApi
// and are not referenced on Android). It presents the phone's paired Bluetooth serial adapters through
// the same SerialPortInfo surface the shared DevicePickerViewModel already uses, so the existing device
// picker lists Bluetooth adapters with no UI changes. The selected entry's PortName is the Bluetooth MAC
// address, which DeviceFactory.CreatePortForDevice routes to a BluetoothSerialPort.
#if ANDROID
using System.Collections.Generic;

namespace PcmHacking;

public class PortDiscovery
{
    public static IEnumerable<SerialPortInfo> GetPorts(ILogger logger)
    {
        List<SerialPortInfo> result = new();

        try
        {
            foreach ((string name, string address) in BluetoothSerialPort.GetPairedDevices())
            {
                // Name is the display label (friendly name + MAC); PortName is the MAC the device
                // factory connects to.
                result.Add(new SerialPortInfo($"{name} ({address})", address));
            }
        }
        catch (System.Exception exception)
        {
            logger.AddDebugMessage("Bluetooth device enumeration failed: " + exception);
        }

        return result;
    }
}

public class SerialPortInfo
{
    public string Name { get; private set; }

    public string? PortName { get; private set; }

    public string DeviceID { get; private set; }

    public int PortNumber { get; private set; }

    /// <summary>Parity with the Windows/Linux constructor: name == port.</summary>
    public SerialPortInfo(string portName)
    {
        this.Name = portName;
        this.DeviceID = portName;
        this.PortName = portName;
        this.PortNumber = 0;
    }

    /// <summary>A friendly display name distinct from the connect target (the Bluetooth MAC).</summary>
    public SerialPortInfo(string name, string portName)
    {
        this.Name = name;
        this.DeviceID = portName;
        this.PortName = portName;
        this.PortNumber = 0;
    }

    public override string ToString() => this.Name;
}
#endif

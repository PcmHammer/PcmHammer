// SPDX-License-Identifier: GPL-3.0-only
using InTheHand.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public class BluetoothDeviceFactory
    {
        public async static Task<Device> CreateBluetoothDevice(string deviceName, ILogger logger)
        {
            BluetoothDeviceInfo bluetoothDeviceInfo = SerialBluetoothDiscovery.GatherPairedDevices().Where(d => d.DeviceName == deviceName).First();
            BluetoothPort port = new(bluetoothDeviceInfo);
            await port.OpenAsync(new BluetoothPortConfiguration(bluetoothDeviceInfo));
            if (deviceName.StartsWith("OBDX"))
            {
                return new OBDXProDevice(port, logger);
            }
            if (deviceName.StartsWith("OBDLink"))
            {
                return new ElmDevice(port, logger);
            }
            return null!;
        }
    }
}

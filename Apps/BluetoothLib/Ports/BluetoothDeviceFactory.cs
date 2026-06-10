using InTheHand.Net;
using InTheHand.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public class BluetoothDeviceFactory
    {
        public async static Task<Device> CreateBluetoothDevice(string deviceAddress, ILogger logger)
        {
            BluetoothDeviceInfo bluetoothDeviceInfo = SerialBluetoothDiscovery.GatherPairedDevices().Where(d => d.DeviceAddress.ToString() == deviceAddress).First();
            BluetoothPort port = new(bluetoothDeviceInfo);
            await port.OpenAsync(new BluetoothPortConfiguration(bluetoothDeviceInfo));
            if (bluetoothDeviceInfo.DeviceName.StartsWith("OBDX"))
            {
                return new OBDXProDevice(port, logger);
            }
            if (bluetoothDeviceInfo.DeviceName.StartsWith("OBDLink"))
            {
                return new ElmDevice(port, logger);
            }
            return null;
        }
    }
}

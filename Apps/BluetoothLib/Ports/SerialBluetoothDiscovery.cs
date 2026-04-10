using InTheHand.Net.Sockets;
using System.Collections;
using System.Collections.Generic;

namespace PcmHacking
{
    public class BluetoothPortConfiguration : PortConfiguration
    {
        public BluetoothDeviceInfo DeviceInfo;

        public BluetoothPortConfiguration(BluetoothDeviceInfo bluetoothDevice) { 
            DeviceInfo = bluetoothDevice;
        }
    }

    public static class SerialBluetoothDiscovery
    {
        private static readonly List<string> _allowedPrefixes = ["OBDX", "OBDLink"];

        public static IEnumerable<BluetoothDeviceInfo> GatherPairedDevices()
        {
            List<BluetoothDeviceInfo> infos = [];
            BluetoothClient bluetoothClient = new();

            foreach (BluetoothDeviceInfo device in bluetoothClient.PairedDevices)
            {
                _allowedPrefixes.ForEach(p =>
                {
                    if (device.DeviceName.StartsWith(p))
                    {
                        infos.Add(device);
                    }
                });
            }
            return infos;
        }
    }
}

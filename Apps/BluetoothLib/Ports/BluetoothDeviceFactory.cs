// SPDX-License-Identifier: GPL-3.0-only
using InTheHand.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public class BluetoothDeviceFactory
    {
        /// <summary>
        /// Open a paired Bluetooth interface and wrap it in the matching device.
        /// </summary>
        /// <param name="deviceType">
        /// A <see cref="DeviceCatalog"/> device-type key, or null/empty to guess from the adapter's
        /// name. Guessing only recognises the factory names ("OBDX...", "OBDLink..."), so a renamed
        /// adapter - or any other CAN-capable one - can only be reached by naming its type.
        /// </param>
        /// <param name="nameOrAddress">
        /// The adapter's address, or its name. An OBDX Pro renames itself between pairings
        /// ("OBDX Pro GT {MAC}" becomes "OBDX Pro GT BLE"), so the address is matched first - a saved
        /// name stops resolving the moment the adapter changes it.
        /// </param>
        /// <returns>The device, or null if it could not be found or typed.</returns>
        public async static Task<Device?> CreateBluetoothDevice(
            string nameOrAddress, string? deviceType, ILogger logger)
        {
            BluetoothDeviceInfo? bluetoothDeviceInfo = SerialBluetoothDiscovery.GatherPairedDevices()
                .FirstOrDefault(d => d.DeviceAddress?.ToString() == nameOrAddress)
                ?? SerialBluetoothDiscovery.GatherPairedDevices()
                .FirstOrDefault(d => d.DeviceName == nameOrAddress);

            if (bluetoothDeviceInfo == null)
            {
                // Was .First(), which threw an unhandled InvalidOperationException here.
                logger.AddUserMessage($"No paired Bluetooth device matches \"{nameOrAddress}\". Re-select it in Settings.");
                return null;
            }

            string deviceName = bluetoothDeviceInfo.DeviceName ?? nameOrAddress;
            BluetoothPort port = new(bluetoothDeviceInfo);
            await port.OpenAsync(new BluetoothPortConfiguration(bluetoothDeviceInfo));

            Device? device = string.IsNullOrEmpty(deviceType)
                ? GuessFromName(port, deviceName, logger)
                : SerialDeviceDetector.CreateKnownDevice(port, deviceType, logger);

            if (device == null)
            {
                logger.AddUserMessage(string.IsNullOrEmpty(deviceType)
                    ? $"Unable to tell what kind of interface \"{deviceName}\" is. Choose its type in Settings."
                    : $"\"{deviceType}\" is not a known interface type.");
                port.Dispose();
            }

            return device;
        }

        /// <summary>Back-compatible overload: guess the device type from the adapter's name.</summary>
        public static Task<Device?> CreateBluetoothDevice(string deviceName, ILogger logger) =>
            CreateBluetoothDevice(deviceName, null, logger);

        private static Device? GuessFromName(BluetoothPort port, string deviceName, ILogger logger)
        {
            if (deviceName.StartsWith("OBDX"))
            {
                return new OBDXProDevice(port, logger);
            }

            if (deviceName.StartsWith("OBDLink"))
            {
                return new ElmDevice(port, logger);
            }

            return null;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// Ported from the Uno logger's Android compatibility layer. The Android stand-in for the Windows
// DeviceFactory (which lives in PcmLibraryWindowsApi and is not referenced on Android). Only the port
// creation differs; the probe sequence and the device-type mapping come from the shared
// SerialDeviceDetector, so an SLCAN or OBDX Pro adapter is recognised here exactly as on desktop.
#if ANDROID
namespace PcmHacking;

public static class DeviceFactory
{
    public static Device? CreateDevice(
        ILogger logger,
        string deviceCategory,
        string serialPort)
    {
        if (deviceCategory == DeviceConstants.DeviceCategorySerial)
        {
            return AutoDetectSerialDevice(serialPort, logger).Result;
        }

        if (deviceCategory == DeviceConstants.DeviceCategoryJ2534)
        {
            logger.AddUserMessage("J2534 is not supported on Android.");
            return null;
        }

        logger.AddUserMessage($"Unsupported device category: {deviceCategory}");
        return null;
    }

    /// <summary>
    /// The flasher's device picker calls this on the J2534 path. Android has no J2534 pass-thru
    /// drivers (those are Windows DLLs), so there is nothing to create; the mobile path is Bluetooth
    /// serial instead. Present as a static stub so the shared DevicePickerViewModel compiles and the
    /// user gets a clear message rather than a missing-method build error.
    /// </summary>
    public static Device? CreateJ2534Device(string? deviceType, ILogger logger)
    {
        logger.AddUserMessage("J2534 is not supported on Android. Use a Bluetooth serial adapter.");
        return null;
    }

    /// <summary>
    /// Create a device of a specific type, skipping detection. This is how a CAN-only adapter that
    /// answers no probes gets selected.
    /// </summary>
    public static Device? CreateSerialDevice(string serialPortName, string? serialPortDeviceType, ILogger logger)
    {
        try
        {
            IPort port = CreatePortForDevice(serialPortName, logger);
            Device? device = SerialDeviceDetector.CreateKnownDevice(port, serialPortDeviceType, logger);
            if (device == null)
            {
                port.Dispose();
            }

            return device;
        }
        catch (Exception exception)
        {
            logger.AddUserMessage($"Unable to create {serialPortDeviceType} on {serialPortName}.");
            logger.AddDebugMessage(exception.ToString());
            return null;
        }
    }

    public async static Task<Device?> AutoDetectSerialDevice(string serialPortName, ILogger logger)
    {
        IPort port = CreatePortForDevice(serialPortName, logger);

        // The port must be disposed on every path that does not hand it to a Device, or the open
        // handle leaks and the next attempt on the same port fails.
        Device? detected = null;
        try
        {
            await port.OpenAsync(SerialDeviceDetector.ProbeConfiguration);
            detected = await SerialDeviceDetector.Detect(port, serialPortName, logger);
            return detected;
        }
        catch (Exception exception)
        {
            logger.AddUserMessage($"Unable to auto-detect a device on {serialPortName}.");
            logger.AddDebugMessage(exception.ToString());
            return null;
        }
        finally
        {
            if (detected == null)
            {
                port.Dispose();
            }
        }
    }

    private static IPort CreatePortForDevice(string serialPortName, ILogger logger)
    {
        if (string.Equals(MockPort.PortName, serialPortName))
        {
            return new MockPort(logger);
        }

        // A Bluetooth MAC (AA:BB:CC:DD:EE:FF) means a paired RFCOMM adapter; anything else is a tty.
        if (LooksLikeBluetoothAddress(serialPortName))
        {
            return new BluetoothSerialPort(serialPortName);
        }

        return new StandardPort(serialPortName);
    }

    private static bool LooksLikeBluetoothAddress(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            value ?? string.Empty, "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$");
}
#endif

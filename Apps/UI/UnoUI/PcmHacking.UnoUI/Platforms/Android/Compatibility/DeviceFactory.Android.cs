// SPDX-License-Identifier: GPL-3.0-only
#if ANDROID
namespace PcmHacking;

/// <summary>
/// The Android stand-in for the Windows DeviceFactory (which lives in PcmLibraryWindowsApi and is not
/// referenced on Android). Only the port creation differs; the probe sequence and the device-type
/// mapping come from the shared <see cref="SerialDeviceDetector"/>, so an SLCAN or OBDX Pro adapter is
/// recognised here exactly as it is on Windows. This file used to carry its own copy of the probes,
/// which had fallen behind and could not detect a CAN-only adapter at all.
/// </summary>
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

        return new StandardPort(serialPortName);
    }
}
#endif

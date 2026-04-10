#if ANDROID
namespace PcmHacking;

public static class DeviceFactory
{
    public static Device? CreateDevice(
        ILogger logger,
        string deviceCategory,
        string serialPort,
        string serialPortDeviceType,
        string j2534DeviceType)
    {
        if (deviceCategory == "Serial")
        {
            return CreateSerialDevice(serialPort, serialPortDeviceType, logger);
        }

        if (deviceCategory == "J2534")
        {
            logger.AddUserMessage("J2534 is not supported on Android.");
            return null;
        }

        logger.AddUserMessage($"Unsupported device category: {deviceCategory}");
        return null;
    }

    private static Device? CreateSerialDevice(string serialPortName, string serialPortDeviceType, ILogger logger)
    {
        try
        {
            IPort port;
            if (string.Equals(MockPort.PortName, serialPortName, StringComparison.Ordinal))
            {
                port = new MockPort(logger);
            }
            else
            {
                port = new StandardPort(serialPortName);
            }

            return serialPortDeviceType switch
            {
                var t when t == OBDXProDevice.DeviceType => new OBDXProDevice(port, logger),
                var t when t == AvtDevice.DeviceType => new AvtDevice(port, logger),
                var t when t == MockDevice.DeviceType => new MockDevice(port, logger),
                var t when t == ElmDevice.DeviceType => new ElmDevice(port, logger),
                _ => null
            };
        }
        catch (Exception exception)
        {
            logger.AddUserMessage($"Unable to create {serialPortDeviceType} on {serialPortName}.");
            logger.AddDebugMessage(exception.ToString());
            return null;
        }
    }
}
#endif

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class DeviceFactory
    {
#if !LINUX_CLI
        /// <summary>
        /// This might not really need to be async. If the J2534 stuff doesn't need it, then this doesn't need it either. Only ised in WinForms.
        /// </summary>
        public static Device? CreateDeviceFromConfigurationSettings(ILogger logger)
        {
            return CreateDevice(SelectionFromSettings(), logger);
        }

        /// <summary>The device selection currently saved in settings.</summary>
        public static DeviceSelection SelectionFromSettings()
        {
            return new DeviceSelection(
                DeviceConfiguration.Settings.DeviceCategory,
                DeviceConfiguration.Settings.J2534DeviceType,
                DeviceConfiguration.Settings.SerialPort,
                DeviceConfiguration.Settings.SerialPortDeviceType);
        }

        /// <summary>
        /// Build the device a selection names, or null when it names none, is incomplete, or is
        /// already open elsewhere in this process.
        /// </summary>
        /// <remarks>
        /// The in-use check lives here rather than only in the UI so that every caller gets it. Two
        /// devices on one interface is not a cosmetic problem: on serial it is an access denial, and
        /// on J2534 the second device's teardown can free the driver library while the first is
        /// still using it.
        /// </remarks>
        public static Device? CreateDevice(DeviceSelection selection, ILogger logger)
        {
            if (selection == null || !selection.IsSelected)
            {
                return null;
            }

            if (DeviceRegistry.IsInUse(selection))
            {
                logger.AddUserMessage(
                    selection.Describe() + " is already in use, so it cannot be opened a second time.");
                return null;
            }

            return CreateDeviceUnchecked(selection, logger);
        }

        /// <summary>
        /// Claim an interface before constructing its device, so concurrent callers cannot both
        /// enter a native driver for the same hardware. The caller owns the returned claim for as
        /// long as the created device remains open.
        /// </summary>
        public static Device? CreateClaimedDevice(
            DeviceSelection selection,
            ILogger logger,
            out IDisposable? claim)
        {
            claim = DeviceRegistry.TryClaim(selection);
            if (claim == null)
            {
                logger.AddUserMessage(
                    selection.Describe() + " is already in use, so it cannot be opened a second time.");
                return null;
            }

            try
            {
                Device? device = CreateDeviceUnchecked(selection, logger);
                if (device == null)
                {
                    claim.Dispose();
                    claim = null;
                }

                return device;
            }
            catch
            {
                claim.Dispose();
                claim = null;
                throw;
            }
        }

        private static Device? CreateDeviceUnchecked(DeviceSelection selection, ILogger logger)
        {
            switch (selection.Category)
            {
                case DeviceConfiguration.Constants.DeviceCategorySerial:
                    return CreateSerialDevice(selection.SerialPort, selection.SerialPortDeviceType, logger);

                case DeviceConfiguration.Constants.DeviceCategoryJ2534:
                    return CreateJ2534Device(selection.J2534DeviceType, logger);

                default:
                    return null;
            }
        }
#endif

        public static Device? CreateDevice(ILogger logger, string deviceCategory, string nameOrPort)
        {
            switch (deviceCategory)
            {
                case DeviceConfiguration.Constants.DeviceCategorySerial:
                    return AutoDetectSerialDevice(nameOrPort, logger).Result;
#if !LINUX_CLI
                case DeviceConfiguration.Constants.DeviceCategoryJ2534:
                    return CreateJ2534Device(nameOrPort, logger);
#endif
                default:
                    return null;
            }
        }

        public static Device? CreateSerialDevice(string? serialPortName, string? serialPortDeviceType, ILogger logger)
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

            // Track the device that ends up owning the port. On EVERY exit path that does not
            // return a device - no match, OR an exception thrown part-way through probing - the
            // port must be disposed. Otherwise the open COM handle leaks and the next attempt to
            // use the same port fails with UnauthorizedAccessException (see serial-port-reopen notes).
            Device? detected = null;
            try
            {
                await port.OpenAsync(SerialDeviceDetector.ProbeConfiguration);

                // The probe sequence itself is shared with the other platforms.
                detected = await SerialDeviceDetector.Detect(port, serialPortName, logger);
                return detected;
            }
            catch (Exception exception)
            {
                // A probe that throws - a port lost or re-opened mid-detect, an unexpected device
                // response - means we could not identify a device on this port. Report it and move on
                // rather than letting it crash auto-detect. The finally still disposes the unowned port.
                logger.AddUserMessage($"Unable to auto-detect a device on {serialPortName}.");
                logger.AddDebugMessage(exception.ToString());
                return null;
            }
            finally
            {
                // No Device took ownership of the port (no match, or an exception was thrown).
                if (detected == null)
                {
                    port.Dispose();
                }
            }
        }

        private static IPort CreatePortForDevice(string? serialPortName, ILogger logger)
        {
            IPort port;
            if (string.Equals(MockPort.PortName, serialPortName))
            {
                port = new MockPort(logger);
            }
            else if (string.Equals(HttpPort.PortName, serialPortName))
            {
                port = new HttpPort(logger);
            }
            else
            {
                port = new StandardPort(serialPortName ?? string.Empty);
            }

            return port;
        }

#if !LINUX_CLI
        public static Device? CreateJ2534Device(string? deviceType, ILogger logger)
        {
            foreach(var device in J2534DeviceFinder.FindInstalledJ2534DLLs(logger))
            {
                if (device.Name == deviceType)
                {
                    return new J2534Device(device, logger);
                }
            }

            return null;
        }
#endif
    }
}

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
        /// <summary>
        /// This might not really need to be async. If the J2534 stuff doesn't need it, then this doesn't need it either. Only ised in WinForms.
        /// </summary>
        public static Device? CreateDeviceFromConfigurationSettings(ILogger logger)
        {
            switch(DeviceConfiguration.Settings.DeviceCategory)
            {
                case DeviceConfiguration.Constants.DeviceCategorySerial:
                    return CreateSerialDevice(DeviceConfiguration.Settings.SerialPort, DeviceConfiguration.Settings.SerialPortDeviceType, logger);

                case DeviceConfiguration.Constants.DeviceCategoryJ2534:
                    return CreateJ2534Device(DeviceConfiguration.Settings.J2534DeviceType, logger);

                default:
                    return null;
            }
        }

        public static Device? CreateDevice(ILogger logger, string deviceCategory, string nameOrPort)
        {
            switch (deviceCategory)
            {
                case DeviceConfiguration.Constants.DeviceCategorySerial:
                    return AutoDetectSerialDevice(nameOrPort, logger).Result;
                case DeviceConfiguration.Constants.DeviceCategoryJ2534:
                    return CreateJ2534Device(nameOrPort, logger);
                default:
                    return null;
            }
        }

        public static Device? CreateSerialDevice(string? serialPortName, string? serialPortDeviceType, ILogger logger)
        {
            try
            {
                IPort port = CreatePortForDevice(serialPortName, logger);

                Device? device;
                switch (serialPortDeviceType)
                {
                    case OBDXProDevice.DeviceType:
                        device = new OBDXProDevice(port, logger);
                        break;

                    case AvtDevice.DeviceType:
                        device = new AvtDevice(port, logger);
                        break;

                    case SlcanDevice.DeviceType:
                        device = new SlcanDevice(port, logger);
                        break;

                    case MockDevice.DeviceType:
                        device = new MockDevice(port, logger);
                        break;

                    case ElmDevice.DeviceType:
                        device = new ElmDevice(port, logger);
                        break;

                    default:
                        device = null;
                        break;
                }

                if (device == null)
                {
                    return null;
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
            SerialPortConfiguration startConfig = new()
            {
                BaudRate = 57600,
                Timeout = 1500
            };
            IPort port = CreatePortForDevice(serialPortName, logger);

            // Track the device that ends up owning the port. On EVERY exit path that does not
            // return a device - no match, OR an exception thrown part-way through probing - the
            // port must be disposed. Otherwise the open COM handle leaks and the next attempt to
            // use the same port fails with UnauthorizedAccessException (see serial-port-reopen notes).
            Device? detected = null;
            try
            {
            await port.OpenAsync(startConfig);

            if(serialPortName == MockPort.PortName)
            {
                return detected = new MockDevice(port, logger);
            }

            AvtDevice avt = new AvtDevice(port, logger);
            if ((await avt.ResetDevice()).Status == ResponseStatus.Success)
            {
                return detected = avt;
            }

            await port.ChangeBaudRate(115200);
            await port.Send(Encoding.ASCII.GetBytes("\r")); // Send this to make sure we have readiness.
            System.Threading.Thread.Sleep(200);
            await port.Send(Encoding.ASCII.GetBytes("AT E0\r")); // Disable echo for these tests.
            System.Threading.Thread.Sleep(200);
            await port.DiscardBuffers();

            // Silence a CAN-only adapter (e.g. SLCAN) that may have been left with its channel open and
            // is streaming bus frames on this port: "C" closes the SLCAN channel. Without this the
            // streamed frames are read as a bogus reply to the identify probes below and the adapter is
            // misdetected as an ELM / ScanTool / AllPro. The command is harmless to the other devices.
            await port.Send(Encoding.ASCII.GetBytes("C\r"));
            System.Threading.Thread.Sleep(200);
            await port.DiscardBuffers();

            string result = await TestIDString(port, "?\r"); // To make sure we fail a OBDX locked in a bad state.
            if (result.Contains("\u007f\u0002"))
            {
                byte[] bytesRead = await TestByteSequence(port, [0x25, 0x00, 0xDA]);
                if (bytesRead.Length == 3 && Utility.CompareArrays(bytesRead, [0x35, 0x00, 0xCA]))
                {
                    return detected = new OBDXProDevice(port, logger);
                }

            }

            result = await TestIDString(port, "STDI\r"); // Only a scantool device will reply correctly.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return detected = new ElmDevice(port, logger);
            }

            result = await TestIDString(port, "AT #1\r"); //Unique to AllPros.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return detected = new ElmDevice(port, logger);
            }

            result = await TestIDString(port, "AT@1\r"); // Not a unique command, but a specific reply.
            if (result.StartsWith("OBDX"))
            {
                return detected = new OBDXProDevice(port, logger);
            }

            result = await TestIDString(port, "AT I"); // Didn't detect any specific known device; generic ELM.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return detected = new ElmDevice(port, logger);
            }

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

        private static async Task<byte[]> TestByteSequence(IPort port, byte[] sendBytes) // Special case use for OBDX reset.
        {
            await port.DiscardBuffers();
            System.Threading.Thread.Sleep(500);
            await port.Send(sendBytes);
            byte[] buffer = new byte[12];
            int bytesRead = await port.Receive(buffer, 0, buffer.Length);
            byte[] result = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, result, 0, bytesRead);
            return result;

        }

        private static async Task<string> TestIDString(IPort port, string idString)
        {
            await port.DiscardBuffers();
            System.Threading.Thread.Sleep(500);
            byte[] buffer = new byte[idString.Length + 2];
            await port.Send(Encoding.ASCII.GetBytes(idString));

            int bytesRead;
            try
            {
                bytesRead = await port.Receive(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                // A device that does not recognise this identify command may not answer at all (a
                // CAN-only adapter, for example). Treat the silence as an empty reply rather than a
                // fault, so auto-detect moves on to the next probe instead of aborting.
                return string.Empty;
            }

            string result = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            // Keep only printable characters. A real ELM / ScanTool / AllPro answers an identify
            // command with a printable string; a CAN-only adapter (e.g. SLCAN) answers an unknown
            // command with a control byte (BELL 0x07), which must not be mistaken for a valid reply.
            StringBuilder printable = new StringBuilder(result.Length);
            foreach (char c in result)
            {
                if (c >= ' ' && c <= '~')
                {
                    printable.Append(c);
                }
            }

            return printable.ToString().Trim();
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
    }
}

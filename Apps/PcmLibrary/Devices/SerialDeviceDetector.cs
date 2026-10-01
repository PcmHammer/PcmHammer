// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Works out which interface is on an already-open port, and builds a device of a named type.
    /// Takes an <see cref="IPort"/> the caller created, because StandardPort is per-platform but the
    /// probe sequence is not.
    /// </summary>
    public static class SerialDeviceDetector
    {
        /// <summary>The baud rate a probe starts at, before stepping up to 115200.</summary>
        public const int InitialBaudRate = 57600;

        /// <summary>The configuration a caller should open the port with before probing.</summary>
        public static SerialPortConfiguration ProbeConfiguration => new SerialPortConfiguration
        {
            BaudRate = InitialBaudRate,
            Timeout = 1500,
        };

        /// <summary>
        /// Build a device of a specific type on an open port. The only way to select an interface that
        /// does not answer the probes. Null if the type is not a known one.
        /// </summary>
        public static Device? CreateKnownDevice(IPort port, string? deviceType, ILogger logger)
        {
            switch (deviceType)
            {
                case OBDXProDevice.DeviceType:
                    return new OBDXProDevice(port, logger);

                case AvtDevice.DeviceType:
                case AvtDevice.DeviceType838: // same driver; the picker split is display-only (model is auto-detected)
                    return new AvtDevice(port, logger);

                case SlcanDevice.DeviceType:
                case "SLCAN (CAN only)": // legacy saved value before the type was renamed to "SLCAN"
                    return new SlcanDevice(port, logger);

                case MockDevice.DeviceType:
                    return new MockDevice(port, logger);

                case ElmDevice.DeviceType:
                    return new ElmDevice(port, logger);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Identify the device on an open port by probing it; null if nothing was identified. The
        /// caller MUST dispose the port on a null result, or the next attempt on it fails with
        /// UnauthorizedAccessException. portName only serves to recognise the mock port.
        /// </summary>
        public static async Task<Device?> Detect(IPort port, string? portName, ILogger logger)
        {
            if (string.Equals(MockPort.PortName, portName))
            {
                return new MockDevice(port, logger);
            }

            AvtDevice avt = new AvtDevice(port, logger);
            if ((await avt.ResetDevice()).Status == ResponseStatus.Success)
            {
                return avt;
            }

            await port.ChangeBaudRate(115200);
            await port.Send(Encoding.ASCII.GetBytes("\r")); // Send this to make sure we have readiness.
            System.Threading.Thread.Sleep(200);

            // An OBDX Pro left in DVI (binary) mode answers every text command with a DVI error
            // frame instead of a reply, so return it to the ELM API blind: 31 02 06 00 = set API
            // protocol to ELM, C6 = checksum. Sent before the AT setup below so that setup reaches
            // the device; other devices see it as junk and ignore it.
            await port.Send(new byte[] { 0x31, 0x02, 0x06, 0x00, 0xC6 });
            System.Threading.Thread.Sleep(200);
            await port.DiscardBuffers();

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

            // Ask for the identity first. The ELM probes below accept any non-empty reply that does
            // not start with "?", so they must not get a chance to claim an OBDX Pro.
            string result = await TestIdString(port, "AT@1\r"); // Identifies an OBDX Pro.
            if (result.StartsWith("OBDX"))
            {
                return new OBDXProDevice(port, logger);
            }

            // A CAN-only SLCAN adapter ignores the AT/ELM probes completely, so it can only be found
            // by its own version command. The ELM family answers "V" with "?", which keeps them out
            // of this branch, and the OBDX Pro has already been claimed above.
            result = await TestIdString(port, "V\r");
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return new SlcanDevice(port, logger);
            }

            result = await TestIdString(port, "STDI\r"); // Only a scantool device will reply correctly.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return new ElmDevice(port, logger);
            }

            result = await TestIdString(port, "AT #1\r"); // Unique to AllPros.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return new ElmDevice(port, logger);
            }

            result = await TestIdString(port, "AT@1\r"); // Not a unique command, but a specific reply.
            if (result.StartsWith("OBDX"))
            {
                return new OBDXProDevice(port, logger);
            }

            result = await TestIdString(port, "AT I"); // Didn't detect any specific known device; generic ELM.
            if (result.Length > 0 && !result.StartsWith("?"))
            {
                return new ElmDevice(port, logger);
            }

            return null;
        }

        private static async Task<string> TestIdString(IPort port, string idString)
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
    }
}

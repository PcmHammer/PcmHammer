// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// A serial SLCAN (Lawicel ASCII) interface, CAN only. These adapters speak a simple line
    /// protocol over a serial/USB-CDC port: open the channel at a bitrate, then exchange CAN frames
    /// as ASCII lines (e.g. "t7E0080102..."). They have no VPW capability, so this device advertises
    /// CAN 500k only and runs the ISO-TP layer in software (IsoTpTransport) over its raw frames
    /// </summary>
    public class SlcanDevice : SerialDevice, ICanChannel, ICanTarget
    {
        public const string DeviceType = "SLCAN (CAN only)";

        // Serial line speed to the adapter (not the CAN bitrate). These high-performance SLCAN
        // adapters run their USB serial link at 2 Mbaud, which is what keeps the ASCII protocol fast.
        private const int SerialBaudRate = 2000000;

        // Lawicel/SLCAN commands. Each is terminated with a carriage return on the wire.
        private const string CmdCloseChannel = "C";
        private const string CmdOpenChannel = "O";
        private const string CmdBitrate500k = "S6"; // S6 = 500 kbit/s
        private const string CmdVersion = "V";

        /// <summary>True once the CAN channel has been opened and configured.</summary>
        private bool canChannelReady;

        /// <summary>CAN id to transmit on (tool to module). Settable so the command layer can retarget.</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN id to accept (module to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        /// <summary>
        /// No-progress wait the ISO-TP transport applies. This adapter has no hardware acceptance
        /// filter, so the transport relies on this to stop reading other modules' traffic once our
        /// conversation goes silent (e.g. after the module resets).
        /// </summary>
        public int ReceiveTimeoutMilliseconds => this.receiveTimeoutMs;

        // Software ISO-TP over this device's raw CAN frames, presenting whole payloads like every
        // other device so Query/filtering work unchanged.
        private readonly IsoTpTransport isoTp;

        // Bytes received from the port but not yet consumed as complete lines. SLCAN replies arrive
        // as carriage-return terminated ASCII lines; a multi-frame burst can deliver several lines (or
        // a partial line) per read, so we buffer across reads and extract whole lines as they complete.
        private readonly List<byte> rxBuffer = new List<byte>();

        /// <summary>How long to wait for the first byte of a response, in milliseconds.</summary>
        private int receiveTimeoutMs = 1000;

        public SlcanDevice(IPort port, ILogger logger) : base(port, logger)
        {
            // The ISO-TP layer does the segmentation, so per-message limits match the other
            // software-ISO-TP CAN devices.
            this.MaxSendSize = 4096 + 10 + 2;
            this.MaxReceiveSize = 4096 + 10 + 2;
            this.Supports4X = false;
            this.isoTp = new IsoTpTransport(this);
        }

        public override string GetDeviceType()
        {
            return DeviceType;
        }

        public override async Task<bool> Initialize()
        {
            this.Logger.AddDebugMessage("Initializing " + this.ToString());

            SerialPortConfiguration configuration = new SerialPortConfiguration
            {
                BaudRate = SerialBaudRate,
                Timeout = 1000
            };
            await this.Port.OpenAsync(configuration);
            await this.Port.DiscardBuffers();

            // Put the adapter into a known state: closing the channel is harmless on a fresh adapter
            // and recovers a confused one. There is no reliable presence check across SLCAN firmwares,
            // so the real confirmation is whether the PCM answers over CAN.
            await this.WriteCommand(CmdCloseChannel);
            await Task.Delay(50);
            this.rxBuffer.Clear();
            await this.Port.DiscardBuffers();

            // Best-effort version, for the log only - never fail the device when it is absent.
            await this.WriteCommand(CmdVersion);
            string? version = await this.ReadLine(300);
            if (version != null && version.StartsWith("V", StringComparison.Ordinal))
            {
                this.Logger.AddDebugMessage("SLCAN version: " + version);
            }
            this.rxBuffer.Clear();
            await this.Port.DiscardBuffers();

            this.Logger.AddUserMessage("SLCAN device ready.");
            return true;
        }

        public override async Task<bool> SendMessage(Message message)
        {
            if (!this.canChannelReady)
            {
                return false;
            }

            // Log the whole payload once; the individual ISO-TP frames are plumbing.
            this.Logger.AddDebugMessage($"TX: {this.TxCanId:X3} {message.GetBytes().ToHex()}");
            return await this.isoTp.SendMessage(message);
        }

        protected override async Task Receive()
        {
            if (!this.canChannelReady)
            {
                return;
            }

            // Reassemble until the inbound filter accepts a message or the bus goes quiet, so a burst
            // of off-conversation traffic is filtered without being mistaken for silence.
            while (true)
            {
                Message? assembled = await this.isoTp.ReceiveMessage();
                if (assembled == null)
                {
                    return;
                }

                // Report the reassembled payload once; the ISO-TP frames are hidden.
                if (this.Enqueue(assembled, logReceived: false))
                {
                    this.Logger.AddDebugMessage($"RX: {this.RxCanId:X3} {assembled.GetBytes().ToHex()}");
                    return;
                }
            }
        }

        /// <summary>SLCAN is CAN only, so only CAN 500k can be monitored.</summary>
        public override IReadOnlyList<BusProtocol> MonitorableProtocols { get; } = new[] { BusProtocol.Can500k };

        /// <summary>
        /// Select the bus protocol. SLCAN is CAN only: CAN 500k opens the channel at 500 kbit/s;
        /// anything else (VPW) is unsupported, so detection skips this device for those buses.
        /// </summary>
        public override async Task<bool> SetProtocol(BusProtocol protocol)
        {
            if (protocol != BusProtocol.Can500k)
            {
                return false;
            }

            if (this.canChannelReady)
            {
                return true;
            }

            // Open the channel: close first (in case it was left open), set 500 kbit/s, then open.
            // Common SLCAN firmwares (e.g. canable) expose no acceptance-filter command and accept all
            // CAN ids in hardware, so off-conversation traffic is dropped by the software id check in
            // ReceiveMessage instead.
            await this.WriteCommand(CmdCloseChannel);
            await this.WriteCommand(CmdBitrate500k);
            await this.WriteCommand(CmdOpenChannel);
            await Task.Delay(50);
            this.rxBuffer.Clear();
            await this.Port.DiscardBuffers();

            this.canChannelReady = true;
            this.Logger.AddDebugMessage($"SLCAN CAN ready: 500k, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}, software ISO-TP.");
            return true;
        }

        public override Task<TimeoutScenario> SetTimeout(TimeoutScenario scenario)
        {
            TimeoutScenario previous = this.currentTimeoutScenario;
            this.currentTimeoutScenario = scenario;
            this.receiveTimeoutMs = (scenario == TimeoutScenario.Detect) ? 500 : 1000;
            return Task.FromResult(previous);
        }

        public override void ClearMessageBuffer()
        {
            // Drop any buffered serial bytes and partial line, but do NOT close the CAN channel.
            this.rxBuffer.Clear();
            this.Port.DiscardBuffers();
        }

        protected override Task<bool> SetVpwSpeedInternal(VpwSpeed newSpeed)
        {
            // CAN only; there is no VPW speed to set.
            return Task.FromResult(false);
        }

        public override Task<bool> IsCommandBroadcasting(byte command)
        {
            // VPW recovery-prompt detection does not apply to a CAN-only interface.
            return Task.FromResult(false);
        }

        public override Task<bool> CheckDeviceConnection()
        {
            // The adapter has no reliable status query; treat an open port as connected. A real
            // problem surfaces when the module does not answer over CAN.
            return Task.FromResult(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    // Close the CAN channel so the adapter stops streaming frames once we release the
                    // port; otherwise the next opener (e.g. auto-detect) reads them as junk. Best-effort.
                    this.Port?.Send(Encoding.ASCII.GetBytes(CmdCloseChannel + "\r")).Wait(200);
                }
                catch
                {
                    // The port is already closing or unusable; nothing more to do.
                }
            }

            base.Dispose(disposing);
        }

        // ---- ICanChannel: raw CAN frame I/O ------------------------------------------------------

        /// <summary>
        /// Transmit one standard (11-bit) CAN frame as an SLCAN line: 't' + 3-hex id + 1-hex DLC +
        /// the data bytes in hex.
        /// </summary>
        public async Task SendCanFrame(uint canId, byte[] framePayload)
        {
            int length = Math.Min(framePayload.Length, 8);

            StringBuilder command = new StringBuilder(5 + (length * 2));
            command.Append('t');
            command.Append((canId & 0x7FF).ToString("X3", CultureInfo.InvariantCulture));
            command.Append(length.ToString("X1", CultureInfo.InvariantCulture));
            for (int i = 0; i < length; i++)
            {
                command.Append(framePayload[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            await this.WriteCommand(command.ToString());
        }

        /// <summary>
        /// Read one CAN frame. Returns (id, data) for the next standard/extended data frame, or
        /// (0, empty) on timeout. Transmit acknowledgements ('z'/'Z'), blank lines, and error markers
        /// are skipped so only real frames surface to the ISO-TP layer.
        /// </summary>
        public async Task<(uint id, byte[] frame)> ReceiveCanFrame()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                int remaining = this.receiveTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return (0u, Array.Empty<byte>());
                }

                string? line = await this.ReadLine(remaining);
                if (line == null)
                {
                    return (0u, Array.Empty<byte>());
                }

                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '?' || line[0] == '\a')
                {
                    this.Logger.AddDebugMessage("SLCAN error response: " + line);
                    continue;
                }

                if (TryParseFrame(line, out uint id, out byte[] data))
                {
                    return (id, data);
                }

                // 'z'/'Z' transmit acks and anything else: not a frame, keep reading.
            }
        }

        /// <summary>
        /// Parse an SLCAN data-frame line. 't' = standard (3-hex id), 'T' = extended (8-hex id),
        /// each followed by a 1-hex DLC and that many data bytes in hex.
        /// </summary>
        private static bool TryParseFrame(string line, out uint id, out byte[] data)
        {
            id = 0u;
            data = Array.Empty<byte>();

            char type = line[0];
            int idLength;
            if (type == 't' || type == 'r')
            {
                idLength = 3;
            }
            else if (type == 'T' || type == 'R')
            {
                idLength = 8;
            }
            else
            {
                return false;
            }

            int dlcIndex = 1 + idLength;
            if (line.Length < dlcIndex + 1)
            {
                return false;
            }

            try
            {
                id = uint.Parse(line.Substring(1, idLength), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int dlc = int.Parse(line.Substring(dlcIndex, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (dlc < 0 || dlc > 8)
                {
                    return false;
                }

                byte[] parsed = new byte[dlc];
                int dataStart = dlcIndex + 1;
                for (int i = 0; i < dlc; i++)
                {
                    int offset = dataStart + (i * 2);
                    if (offset + 2 > line.Length)
                    {
                        return false;
                    }

                    parsed[i] = byte.Parse(line.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }

                data = parsed;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // ---- Serial line I/O ---------------------------------------------------------------------

        /// <summary>Send one SLCAN command, appending the carriage-return terminator.</summary>
        private async Task WriteCommand(string command)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command + "\r");
            await this.Port.Send(bytes);
        }

        /// <summary>
        /// Read the next carriage-return terminated line from the port, buffering across reads, or
        /// null if none arrives within the timeout. Reads all available bytes per poll for throughput.
        /// </summary>
        private async Task<string?> ReadLine(int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                int cut = this.rxBuffer.FindIndex(b => b == (byte)'\r' || b == (byte)'\n');
                if (cut >= 0)
                {
                    string line = Encoding.ASCII.GetString(this.rxBuffer.GetRange(0, cut).ToArray());
                    this.rxBuffer.RemoveRange(0, cut + 1);
                    return line;
                }

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return null;
                }

                int available = await this.Port.GetReceiveQueueSize();
                if (available > 0)
                {
                    byte[] buffer = new byte[available];
                    int read = await this.Port.Receive(buffer, 0, available);
                    for (int i = 0; i < read; i++)
                    {
                        this.rxBuffer.Add(buffer[i]);
                    }
                }
            }
        }
    }
}

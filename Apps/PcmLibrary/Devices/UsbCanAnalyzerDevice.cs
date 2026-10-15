// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The USB-CAN Analyzer: a CH340-based serial CAN adapter, CAN only. Sold by Seeed Studio as the
    /// "USB-CAN Analyzer" part 114991193, by Waveshare as the "USB-CAN-A", and unbranded; all share one firmware
    /// protocol. Frames cross the serial link as AA [type] [id] [data] 55, and the adapter is
    /// configured by a 20-byte command rather than a text protocol.
    /// </summary>
    /// <remarks>
    /// Structured like <see cref="SlcanDevice"/>: a serial CAN adapter with no native ISO-TP and no
    /// hardware acceptance filter, so it advertises CAN 500k only and runs
    /// <see cref="IsoTpTransport"/> in software over its raw frames.
    ///
    /// The receive decoder is <see cref="CanParser"/>, which predates this device: PcmLogger used it
    /// to sniff this adapter over a serial port of its own. That parallel path is what this class
    /// replaces, so one interface selection serves both logging and PCM access.
    ///
    /// Vendor documentation (Seeed's "USB (Serial port) to CAN protocol defines") covers only the two
    /// data-frame formats. The configuration command is not vendor-documented; its layout here
    /// follows two independent working implementations (kobolt/usb-can and python-can's seeedstudio
    /// interface), which agree byte for byte. The checksum range is corroborated by arithmetic: both
    /// worked examples of the vendor's 20-byte data frame sum bytes 2..18.
    /// </remarks>
    public class UsbCanAnalyzerDevice : SerialDevice, ICanChannel, ICanTarget
    {
        public const string DeviceType = "USBCAN";

        /// <summary>
        /// Serial line speed to the adapter, not the CAN bitrate. 2 Mbaud is the factory default and
        /// the only speed fast enough to carry a busy 500k bus. It is persistent and the vendor tool
        /// can change it (down to 9600), so an adapter someone has reconfigured will not answer here;
        /// shorting the DEFAULT pad on the board restores it.
        /// </summary>
        private const int SerialBaudRate = 2000000;

        /// <summary>Id and payload of the frame the detection probe loops back through the adapter.</summary>
        private const uint ProbeCanId = 0x7FF;

        private static readonly byte[] ProbePayload = { 0x50, 0x63, 0x6D, 0x48, 0x61, 0x6D, 0x72, 0x21 };

        /// <summary>True once the CAN channel has been configured for normal operation.</summary>
        private bool canChannelReady;

        /// <summary>CAN id to transmit on (tool to module). Settable so the command layer can retarget.</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN id to accept (module to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        /// <summary>ISO-TP addressing for transmitted frames.</summary>
        public IsoTpAddressing TxAddressing { get; set; } = IsoTpAddressing.Normal;

        /// <summary>ISO-TP addressing for received frames.</summary>
        public IsoTpAddressing RxAddressing { get; set; } = IsoTpAddressing.Normal;

        /// <summary>Software ISO-TP builds every frame here, so any addressing can be framed.</summary>
        public bool SupportsExtendedAddressing => true;

        /// <summary>
        /// No-progress wait the ISO-TP transport applies. The adapter's acceptance filter is left
        /// wide open, so every frame on the bus arrives and the transport needs this to stop reading
        /// other modules' traffic once our own conversation goes quiet.
        /// </summary>
        public int ReceiveTimeoutMilliseconds => this.receiveTimeoutMs;

        private readonly IsoTpTransport isoTp;

        /// <summary>
        /// Decodes the adapter's byte stream into frames. Stateful across reads: one serial read can
        /// deliver part of a frame, or several.
        /// </summary>
        private readonly CanParser parser = new CanParser();

        private int receiveTimeoutMs = 1000;

        public UsbCanAnalyzerDevice(IPort port, ILogger logger) : base(port, logger)
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
                Timeout = 1000,
            };

            await this.Port.OpenAsync(configuration);
            await this.Port.DiscardBuffers();

            this.Logger.AddUserMessage("USB-CAN Analyzer ready.");
            return true;
        }

        /// <summary>CAN only, so only CAN 500k can be monitored.</summary>
        public override IReadOnlyList<BusProtocol> MonitorableProtocols { get; } = new[] { BusProtocol.Can500k };

        /// <summary>
        /// Select the bus protocol. CAN 500k configures the adapter for normal operation; anything
        /// else (VPW) is unsupported, so detection skips this device for those buses.
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

            await this.Configure(UsbCanAnalyzerProtocol.WorkModeNormal);

            this.canChannelReady = true;
            this.Logger.AddDebugMessage(
                $"USB-CAN Analyzer ready: 500k, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}, software ISO-TP.");
            return true;
        }

        /// <summary>
        /// Monitoring needs nothing special: the adapter has no acceptance filter, so every frame on
        /// the bus already arrives.
        /// </summary>
        public override async Task<bool> BeginMonitor(BusProtocol protocol)
        {
            return await this.SetProtocol(protocol);
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

                if (this.Enqueue(assembled, logReceived: false))
                {
                    this.Logger.AddDebugMessage($"RX: {this.RxCanId:X3} {assembled.GetBytes().ToHex()}");
                    return;
                }
            }
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
            this.Port.DiscardBuffers();
        }

        protected override Task<bool> SetVPWSpeedInternal(VPWSpeed newSpeed)
        {
            // CAN only; there is no VPW speed to set.
            return Task.FromResult(false);
        }

        public override Task<byte?> ReadBroadcastState(byte command)
        {
            // CAN PCMs answer a $A2 request with $E2 rather than broadcasting unprompted, so this
            // passive listen cannot find them.
            return Task.FromResult<byte?>(null);
        }

        public override Task<bool> CheckDeviceConnection()
        {
            // The adapter answers no status query at all - see Probe for why identification has to be
            // an active loopback. Treat an open port as connected; a real problem surfaces when the
            // module does not answer over CAN.
            return Task.FromResult(true);
        }

        // ---- Detection ---------------------------------------------------------------------------

        /// <summary>
        /// Decide whether the adapter on an open port is a USB-CAN Analyzer, by having it loop a
        /// known frame back to us. The port is left at this device's baud rate.
        /// </summary>
        /// <remarks>
        /// There is nothing to read for identification: across the whole vendor document set the
        /// adapter has no version command, no identity query and no reply of any kind - configuration
        /// is one-way, host to device. Its only passive signature is the CH340's USB id (1A86:7523),
        /// which thousands of unrelated devices share.
        ///
        /// So identification has to be active, and the vendor supplies a safe way to do it: loopback
        /// + silent mode is documented as a "hot self test ... does not affect the CAN bus system",
        /// where the controller only ever sends recessive bits. The probe can therefore run with a
        /// vehicle connected without putting a single bit on the bus. Getting our own nonce back in
        /// this framing is conclusive - no other interface in the catalog echoes it - and it proves
        /// the baud rate and the frame format at the same time.
        /// </remarks>
        public static async Task<bool> Probe(IPort port, ILogger logger)
        {
            try
            {
                await port.ChangeBaudRate(SerialBaudRate);
                await port.DiscardBuffers();

                // Loopback + silent: nothing reaches the bus, and our own frame comes back.
                await port.Send(UsbCanAnalyzerProtocol.BuildConfiguration(UsbCanAnalyzerProtocol.WorkModeLoopbackSilent));
                await Task.Delay(100);
                await port.DiscardBuffers();

                await port.Send(UsbCanAnalyzerProtocol.BuildDataFrame(ProbeCanId, ProbePayload));

                CanParser parser = new CanParser();
                Stopwatch stopwatch = Stopwatch.StartNew();
                byte[] buffer = new byte[64];

                while (stopwatch.ElapsedMilliseconds < 500)
                {
                    int available = await port.GetReceiveQueueSize();
                    if (available == 0)
                    {
                        continue;
                    }

                    int read = await port.Receive(buffer, 0, Math.Min(available, buffer.Length));
                    for (int index = 0; index < read; index++)
                    {
                        if (!parser.IsCompleteMessage(buffer[index], out CanMessage message))
                        {
                            continue;
                        }

                        if (message.MessageId != ProbeCanId || message.Payload.Length != ProbePayload.Length)
                        {
                            continue;
                        }

                        for (int i = 0; i < ProbePayload.Length; i++)
                        {
                            if (message.Payload[i] != ProbePayload[i])
                            {
                                return false;
                            }
                        }

                        logger.AddDebugMessage("USB-CAN Analyzer identified by loopback.");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception exception)
            {
                // A port that cannot take 2 Mbaud, or an adapter in transparent-transmission mode:
                // not this device as far as detection is concerned.
                logger.AddDebugMessage("USB-CAN Analyzer probe failed: " + exception.Message);
                return false;
            }
        }

        // ---- ICanChannel: raw CAN frame I/O ------------------------------------------------------

        public async Task SendCanFrame(uint canId, byte[] framePayload)
        {
            await this.Port.Send(UsbCanAnalyzerProtocol.BuildDataFrame(canId, framePayload));
        }

        /// <summary>
        /// Read one CAN frame, or (0, empty) on timeout. Every frame on the bus arrives here; the
        /// ISO-TP layer discards the ones that are not part of our conversation.
        /// </summary>
        public async Task<(uint id, byte[] frame)> ReceiveCanFrame()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            byte[] buffer = new byte[256];

            while (stopwatch.ElapsedMilliseconds < this.receiveTimeoutMs)
            {
                int available = await this.Port.GetReceiveQueueSize();
                if (available == 0)
                {
                    continue;
                }

                int read = await this.Port.Receive(buffer, 0, Math.Min(available, buffer.Length));
                for (int index = 0; index < read; index++)
                {
                    if (this.parser.IsCompleteMessage(buffer[index], out CanMessage message))
                    {
                        // Remote frames carry no data and mean nothing to ISO-TP.
                        if (message.IsRemoteFrame)
                        {
                            continue;
                        }

                        return (message.MessageId, message.Payload.ToArray());
                    }
                }
            }

            return (0u, Array.Empty<byte>());
        }

        /// <summary>
        /// Send the configuration command and let the adapter settle. The adapter never acknowledges
        /// it, so there is nothing to confirm; a wrong configuration shows up as silence on the bus.
        /// </summary>
        private async Task Configure(byte mode)
        {
            await this.Port.Send(UsbCanAnalyzerProtocol.BuildConfiguration(mode));
            await Task.Delay(100);
            await this.Port.DiscardBuffers();
        }

    }
}

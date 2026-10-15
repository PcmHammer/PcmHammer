// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// This class encapsulates all code that is unique to the AVT 852 interface.
    /// </summary>
    /// 
    public class AvtDevice : SerialDevice, ICanChannel, ICanTarget
    {
        // One driver, two picker entries: the 838 is VPW-only, the 842/852 also do CAN. They are
        // distinct keys (the picker persists the key and must tell the two apart) but both create this
        // same AvtDevice, which detects the actual model on reset. DeviceType is kept as the 842/852
        // key so existing saved settings still resolve.
        public const string DeviceType = "AVT (838/842/852)";
        public const string DeviceType838 = "AVT 838";
        public short Model = 0; // 0 = unknown or 838, 842, 852

        public static readonly Message AVT_RESET                = new Message(new byte[] { 0xF1, 0xA5 });
        public static readonly Message AVT_ENTER_VPW_MODE       = new Message(new byte[] { 0xE1, 0x33 });
        public static readonly Message AVT_ENTER_CAN_MODE       = new Message(new byte[] { 0xE1, 0x99 });
        // CAN0 setup (AVT-85x manual 7.10). The acceptance-ID command is built at runtime from
        // RxCanId; these are the fixed steps. We run the firmware in raw-frame mode and do ISO-TP
        // in software (IsoTpTransport), since the firmware ISO-15765 mode only delivered the first
        // frame of a multi-frame response.
        public static readonly Message AVT_CAN0_500K            = new Message(new byte[] { 0x73, 0x0A, 0x00, 0x02 }); // CAN0 baud = 500 kbaud
        public static readonly Message AVT_CAN0_IDMASK_MODE4    = new Message(new byte[] { 0x73, 0x2B, 0x00, 0x04 }); // CAN0 ID/Mask mode 4 (16-bit IDs)
        public static readonly Message AVT_CAN0_MASK0_EXACT     = new Message(new byte[] { 0x75, 0x2C, 0x00, 0x00, 0x00, 0x00 }); // CAN0 Mask0 = must-match all bits
        // Mask bits are don't-care when set, so 07FF ignores all 11 ID bits. The 0x40 makes RTR
        // don't-care while leaving IDE must-match, so only 11-bit frames arrive. For monitoring.
        public static readonly Message AVT_CAN0_MASK0_ANY       = new Message(new byte[] { 0x75, 0x2C, 0x40, 0x00, 0x07, 0xFF }); // CAN0 Mask0 = any 11-bit ID
        public static readonly Message AVT_CAN0_ACCEPT_ANY      = new Message(new byte[] { 0x75, 0x2A, 0x00, 0x00, 0x00, 0x00 }); // CAN0 acceptance ID0 = 0
        public static readonly Message AVT_CAN0_ISO15765_OFF    = new Message(new byte[] { 0x73, 0x26, 0x00, 0x00 }); // CAN0 ISO 15765 off (raw frames)
        public static readonly Message AVT_ENABLE_CAN0          = new Message(new byte[] { 0x73, 0x11, 0x00, 0x01 }); // CAN0 normal mode
        public static readonly Message AVT_REQUEST_MODEL        = new Message(new byte[] { 0xF0 });
        public static readonly Message AVT_REQUEST_FIRMWARE     = new Message(new byte[] { 0xB0 });
        public static readonly Message AVT_DISABLE_TX_ACK       = new Message(new byte[] { 0x52, 0x40, 0x00 });
        public static readonly Message AVT_FILTER_DEST          = new Message(new byte[] { 0x52, 0x5B, DeviceId.Tool });
        public static readonly Message AVT_1X_SPEED             = new Message(new byte[] { 0xC1, 0x00 });
        public static readonly Message AVT_4X_SPEED             = new Message(new byte[] { 0xC1, 0x01 });

        // AVT reader strips the header
        public static readonly Message AVT_VPW                  = new Message(new byte[] { 0x07 });       // 91 07
        public static readonly Message AVT_852_IDLE             = new Message(new byte[] { 0x27 });       // 91 27
        public static readonly Message AVT_842_IDLE             = new Message(new byte[] { 0x12 });       // 91 12
        public static readonly Message AVT_FIRMWARE             = new Message(new byte[] { 0x04 });       // 92 04 15 (firmware 1.5)
        public static readonly Message AVT_TX_ACK               = new Message(new byte[] { 0x60 });       // 01 60
        public static readonly Message AVT_FILTER_DEST_OK       = new Message(new byte[] { 0x5B, DeviceId.Tool });// 62 5B F0
        public static readonly Message AVT_DISABLE_TX_ACK_OK    = new Message(new byte[] { 0x40, 0x00 }); // 62 40 00
        public static readonly Message AVT_BLOCK_TX_ACK         = new Message(new byte[] { 0xF3, 0x60 }); // F3 60

        /// <summary>Current bus protocol; drives the send/receive format in this class.</summary>
        protected BusProtocol CurrentProtocol { get; private set; } = BusProtocol.VPW;

        // CAN target addresses. Default to the standard OBD2 PCM IDs (from the shared CanId
        // constants), but are settable so the command layer can address a different module or ID.
        /// <summary>CAN ID to transmit on (tool to target).</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN ID to accept (target to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        /// <summary>ISO-TP addressing for transmitted frames.</summary>
        public IsoTpAddressing TxAddressing { get; set; } = IsoTpAddressing.Normal;

        /// <summary>ISO-TP addressing for received frames.</summary>
        public IsoTpAddressing RxAddressing { get; set; } = IsoTpAddressing.Normal;

        /// <summary>Software ISO-TP builds every frame here, so any addressing can be framed.</summary>
        public bool SupportsExtendedAddressing => true;

        /// <summary>No-progress wait the ISO-TP transport applies (the same budget ReadAVTPacket uses).</summary>
        public int ReceiveTimeoutMilliseconds => this.receiveTimeoutMs;

        // Serial bytes drained from the port but not yet parsed into whole AVT CAN packets. The CAN
        // receive path reads the port in bulk and slices frames out of this, so it pays the serial
        // round-trip once per burst rather than per frame. Only the CAN path uses it; VPW reads still
        // go through ReadAVTPacket.
        private readonly List<byte> canRxBuffer = new List<byte>();

        // Software ISO-TP over this device's raw CAN frames. The AVT firmware can do ISO-TP itself
        // (ISO-15765 mode) but only delivered the first frame of a multi-frame response, so we run
        // it in raw-frame mode and reassemble here, presenting whole payloads like every device.
        private readonly IsoTpTransport isoTp;

        public AvtDevice(IPort port, ILogger logger) : base(port, logger)
        {
            this.MaxSendSize = 4096+10+2;    // packets up to 4112 but we want 4096 byte data blocks
            this.MaxReceiveSize = 4096+10+2; // with 10 byte header and 2 byte block checksum
            this.Supports4X = true;
            this.SupportsSingleDpidLogging = true;
            this.SupportsStreamLogging = true;
            this.isoTp = new IsoTpTransport(this);
        }

        public override string GetDeviceType()
        {
            return DeviceType;
        }

        public override async Task<bool> Initialize()
        {
            this.Logger.AddDebugMessage("Initializing " + this.ToString());

            Response<Message> m;

            SerialPortConfiguration configuration = new SerialPortConfiguration
            {
                BaudRate = 57600 // default RS232 speed for 838, 842. ignored by the USB 852.
            };
            await this.Port.OpenAsync(configuration);
            await this.Port.DiscardBuffers();

            m = await ResetDevice();
            if (m.Status == ResponseStatus.Error)
            {
                return false;
            }

            this.Logger.AddDebugMessage("Looking for Firmware message");
            if (this.Model == 838)
            {
                await this.Port.Send(AvtDevice.AVT_REQUEST_FIRMWARE.GetBytes()); // we need to request this on 838 but the 852 sends it without being asked. 842 needs testing.
            }

            m = await this.FindResponse(AVT_FIRMWARE);
            if (m.Status == ResponseStatus.Success)
            {
                byte firmware = m.Value.GetBytes()[1];
                int major = firmware >> 4;
                int minor = firmware & 0x0F;
                this.Logger.AddUserMessage("AVT Firmware " + major + "." + minor);
            }
            else
            {
                this.Logger.AddUserMessage("Firmware not found or failed reset");
                this.Logger.AddDebugMessage("Expected " + AVT_FIRMWARE.GetBytes());
                return false;
            }

            // 838 defaults to vpw mode, so dont set it on that device.
            if (this.Model != 838)
            {
                await this.Port.Send(AvtDevice.AVT_ENTER_VPW_MODE.GetBytes());
                m = await FindResponse(AVT_VPW);
                if (m.Status == ResponseStatus.Success)
                {
                    this.Logger.AddDebugMessage("Set VPW Mode");
                }
                else
                {
                    this.Logger.AddUserMessage("Unable to set AVT device to VPW mode");
                    this.Logger.AddDebugMessage("Expected " + AvtDevice.AVT_VPW.ToString());
                    return false;
                }
            }

            await AVTSetup();

            return true;
        }

        public async Task<Response<Message>> ResetDevice()
        {
            Response<Message> m;
            this.Logger.AddDebugMessage("Sending 'reset' message.");
            await this.Port.Send(AvtDevice.AVT_RESET.GetBytes());
            m = await ReadAVTPacket();
            if (m.Status == ResponseStatus.Success)
            {
                switch (m.Value.GetBytes()[0])
                {
                    case 0x27:
                        this.Logger.AddUserMessage("AVT 852 Reset OK");
                        this.Model = 852;
                        break;
                    case 0x12:
                        this.Logger.AddUserMessage("AVT 842 Reset OK");
                        this.Model = 842;
                        break;
                    case 0x07:
                        this.Logger.AddUserMessage("AVT 838 Reset OK");
                        this.Model = 838;
                        this.MaxSendSize = 2048 + 10 + 2;
                        this.MaxReceiveSize = 2048 + 10 + 2;
                        break;
                    default:
                        this.Logger.AddUserMessage("Unknown and unsupported AVT device detected. Please add support and submit a patch!");
                        return Response.Create(ResponseStatus.Error, (Message)null!);
                }
            }
            else
            {
                this.Logger.AddUserMessage("AVT device not found or failed reset");
                return Response.Create(ResponseStatus.Error, (Message)null!);
            }

            return Response.Create(ResponseStatus.Success, m.Value);
        }

        /// <summary>How long ReadAVTPacket waits for the first byte of a packet, in milliseconds.</summary>
        private int receiveTimeoutMs = 1000;

        /// <summary>
        /// Set the receive timeout for the given scenario. Bounds ReadAVTPacket's first-byte wait;
        /// only the scan-probe budget (Detect) differs from the existing 1000 ms default, so normal
        /// operations are unchanged.
        /// </summary>
        public override Task<TimeoutScenario> SetTimeout(TimeoutScenario scenario)
        {
            TimeoutScenario previous = this.currentTimeoutScenario;
            this.currentTimeoutScenario = scenario;
            this.receiveTimeoutMs = (scenario == TimeoutScenario.Detect) ? 500 : 1000;
            return Task.FromResult(previous);
        }

        /// <summary>
        /// This will process incoming messages for up to 500ms looking for a message
        /// </summary>
        public async Task<Response<Message>> FindResponse(Message expected)
        {
            //this.Logger.AddDebugMessage("FindResponse called");

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            while(stopwatch.ElapsedMilliseconds < 3000)
            {
                Response<Message> response = await this.ReadAVTPacket();
                if (response.Status == ResponseStatus.Success) 
                    if (Utility.CompareArraysPart(response.Value.GetBytes(), expected.GetBytes()))
                        return Response.Create(ResponseStatus.Success, (Message) response.Value);
                await Task.Delay(100);
            }

            return Response.Create(ResponseStatus.Timeout, (Message) null!);
        }

        /// <summary>
        /// Read an AVT formatted packet from the interface, and return a Response/Message
        /// </summary>
        async private Task<Response<Message>> ReadAVTPacket()
        {

            //this.Logger.AddDebugMessage("Trace: ReadAVTPacket");
            int length = 0;
            bool status = true; // do we have a status byte? (we dont for some 9x init commands)
            byte[] rx = new byte[2]; // we dont read more than 2 bytes at a time

            // Get the first packet byte.
            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                while (sw.ElapsedMilliseconds < this.receiveTimeoutMs)
                {
                    if (await this.Port.GetReceiveQueueSize() > 0) { break;}
                }
                if (await this.Port.GetReceiveQueueSize() > 0)
                {
                    await this.Port.Receive(rx, 0, 1);
                }
                else
                {
                    this.Logger.AddDebugMessage("Waited 2seconds.. no data present");
                    return Response.Create(ResponseStatus.Timeout, (Message)null!);
                }
            }
            catch (Exception) // timeout exception - log no data, return error.
            {
                this.Logger.AddDebugMessage("No Data");
                return Response.Create(ResponseStatus.Timeout, (Message)null!);
            }

            // read an AVT format length
            switch (rx[0])
            {
                case 0x11:
                    await this.Port.Receive(rx, 0, 1);
                    length = rx[0];
                    break;
                case 0x12:
                    await this.Port.Receive(rx, 0, 1);
                    length = rx[0] << 8;
                    await this.Port.Receive(rx, 0, 1);
                    length += rx[0];
                    break;
                default:
                    //this.Logger.AddDebugMessage("RX: Header " + rx[0].ToString("X2"));
                    int type = rx[0] >> 4;
                    switch (type) {
                        case 0xF: // standard < 16 byte data packet (AVT 838)
                        case 0x0: // standard < 16 byte data packet (AVT 842/852)
                            length = rx[0] & 0x0F;
                            break;
                        case 0x2:
                            length = rx[0] & 0x0F;
                            status = false;
                            break;
                        case 0x3: // Invalid Command
                            length = rx[0] & 0x0F;
                            byte[] r = new byte[length];
                            await this.Port.Receive(r, 0, 1);
                            this.Logger.AddDebugMessage("RX: Invalid command. Packet that began with  " + r.ToHex() + " was rejected by the AVT");
                            return Response.Create(ResponseStatus.Error, new Message(r));
                        case 0x6: // avt filter
                            length = rx[0] & 0x0F;
                            status = false;
                            break;
                        case 0x8: // high speed notifications
                            length = rx[0] & 0x0F;
                            length--;
                            status = false;
                            break;
                        case 0x9: // init and version
                            length = rx[0] & 0x0F;
                            status = false;
                            break;
                        case 0xC: // C1 01 for 4x OK
                            length = rx[0] & 0x0F;
                            status = false;
                            break;
                        default:
                            this.Logger.AddDebugMessage("RX: Unhandled packet type " + type + ". Add support to ReadAVTPacket()");
                            status = false; // all non-zero high nibble type bytes have no status
                            break;
                    }
                    break;
            }

            // CAN receive frames carry no VPW-style status byte: the byte after the length is the
            // flags/channel byte. Never strip a status byte in CAN mode or the ID and payload shift.
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                status = false;
            }

            // if we need to get check and discard the status byte
            if (status == true)
            {
                length--;
                await this.Port.Receive(rx, 0, 1);
                if (rx[0] != 0) this.Logger.AddDebugMessage("RX: bad packet status: " + rx[0].ToString("X2"));
            }

            if (length <= 0) {
                this.Logger.AddDebugMessage("Not reading " + length + " byte packet");
                return Response.Create(ResponseStatus.Error, (Message)null!);
            }

            // build a complete packet
            byte[] receive = new byte[length];
            byte[] packet = new byte[length];
            int bytes;
            DateTime start = DateTime.Now;
            DateTime stop = start + TimeSpan.FromSeconds(2);
            for (int i = 0; i < length; )
            {
                if (DateTime.Now > stop) return Response.Create(ResponseStatus.Timeout, (Message)null!);
                bytes = await this.Port.Receive(receive, 0, length);
                Buffer.BlockCopy(receive, 0, packet, i, bytes);
                i += bytes;
            }
            
            //this.Logger.AddDebugMessage("Total Length=" + length + " RX: " + packet.ToHex());
            return Response.Create(ResponseStatus.Success, new Message(packet));
        }

        /// <summary>
        /// Convert a Message to an AVT formatted transmit, and send to the interface
        /// </summary>
        async private Task<Response<Message>> SendAVTPacket(Message message)
        {
            //this.Logger.AddDebugMessage("Trace: SendAVTPacket");

            byte[] txb = { 0x12 };
            int length = message.GetBytes().Length;

            if (length > 0xFF)
            {
                await this.Port.Send(txb);
                txb[0] = unchecked((byte)(length >> 8));
                await this.Port.Send(txb);
                txb[0] = unchecked((byte)(length & 0xFF));
                await this.Port.Send(txb);
            }
            else if (length > 0x0F)
            {
                txb[0] = (byte)(0x11);
                await this.Port.Send(txb);
                txb[0] = unchecked((byte)(length & 0xFF));
                await this.Port.Send(txb);
            }
            else
            {
                txb[0] = unchecked((byte)(length & 0x0F));
                await this.Port.Send(txb);
            }

            //this.Logger.AddDebugMessage("send: " + message.GetBytes().ToHex());
            await this.Port.Send(message.GetBytes());
            
            return Response.Create(ResponseStatus.Success, message);
        }

        /// <summary>
        /// Configure AVT to return only packets targeted to the tool (Device ID F0), and disable transmit acks
        /// </summary>
        async private Task<Response<Boolean>> AVTSetup()
        {
            //this.Logger.AddDebugMessage("AVTSetup called");

            this.Logger.AddDebugMessage("Disable AVT Acks");
            await this.Port.Send(AVT_DISABLE_TX_ACK.GetBytes());
            Response<Message> m = await this.FindResponse(AVT_DISABLE_TX_ACK_OK);
            if (m.Status == ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("AVT Acks disabled");
            }
            else
            {
                this.Logger.AddUserMessage("Could not disable ACKs");
                this.Logger.AddDebugMessage("Expected " + AVT_DISABLE_TX_ACK_OK.ToString());
                return Response.Create(ResponseStatus.Error, false);
            }

            this.Logger.AddDebugMessage("Configure AVT filter");
            await this.Port.Send(AVT_FILTER_DEST.GetBytes());
            m = await this.FindResponse(AVT_FILTER_DEST_OK);
            if (m.Status == ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("AVT filter configured");
            }
            else
            {
                this.Logger.AddUserMessage("Could not configure AVT filter");
                this.Logger.AddDebugMessage("Expected " + AVT_FILTER_DEST_OK.ToString());
                return Response.Create(ResponseStatus.Error, false);
            }

            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Send a message, wait for a response, return the response.
        /// </summary>
        public override async Task<bool> SendMessage(Message message)
        {
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                // Log the whole payload once; the individual ISO-TP frames are plumbing.
                this.Logger.AddDebugMessage($"TX: {this.TxCanId:X3} {message.GetBytes().ToHex()}");
                return await this.isoTp.SendMessage(message);
            }

            this.Logger.AddDebugMessage("TX: " + message.GetBytes().ToHex());
            await SendAVTPacket(message);
            return true;
        }

        protected async override Task Receive()
        {
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                // Keep reassembling until the active inbound filter accepts one (the reply we are
                // waiting for) or the bus goes quiet, so a burst of off-conversation traffic is
                // filtered out without being mistaken for silence.
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

            Response<Message> response = await ReadAVTPacket();
            if (response.Status == ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("RX: " + response.Value.GetBytes().ToHex());
                this.Enqueue(response.Value);
                return;
            }

            this.Logger.AddDebugMessage("AVT: no message waiting.");
        }
        
        /// <summary>
        /// Set the interface to low (false) or high (true) speed
        /// </summary>
        /// <remarks>
        /// The caller must also tell the PCM to switch speeds
        /// </remarks>
        protected override async Task<bool> SetVPWSpeedInternal(VPWSpeed newSpeed)
        {

            if (newSpeed == VPWSpeed.Standard)
            {
                this.Logger.AddDebugMessage("AVT setting VPW 1X");
                await this.Port.Send(AvtDevice.AVT_1X_SPEED.GetBytes());
                await ReadAVTPacket(); // C1 00 (switched to 1x)
            }
            else
            {
                await ReadAVTPacket(); // 23 83 00 20 AVT generated response from generic PCM switch high speed command in Vehicle.cs
                this.Logger.AddDebugMessage("AVT setting VPW 4X");
                await this.Port.Send(AvtDevice.AVT_4X_SPEED.GetBytes());
                await ReadAVTPacket(); // C1 01 (switched to 4x)
            }

            return true;
        }

        public override void ClearMessageBuffer()
        {
            this.canRxBuffer.Clear();
            this.Port.DiscardBuffers();
            System.Threading.Thread.Sleep(50);
        }

        // This needs testing, but in theory should work.
        public override async Task<byte?> ReadBroadcastState(byte command)
        {
            this.ClearMessageQueue();
            Message incoming = await ReceiveMessage();
            return MatchBroadcast(incoming, command);
        }

        // There might be a better way to achieve this with AVT.
        public async override Task<bool> CheckDeviceConnection()
        {
            Response<Message> m = await ResetDevice();
            if (m.Status == ResponseStatus.Error)
            {
                return false;
            }
            return true;
        }

        /// <summary>VPW and CAN 500k can both be monitored on this device.</summary>
        public override IReadOnlyList<BusProtocol> MonitorableProtocols { get; } = new[] { BusProtocol.VPW, BusProtocol.Can500k };

        /// <summary>
        /// Begin monitoring. The destination filter set at startup only passes frames addressed to
        /// the tool, so a passive monitor would see its own conversation rather than the bus.
        /// </summary>
        public override async Task<bool> BeginMonitor(BusProtocol protocol)
        {
            if (!await this.SetProtocol(protocol))
            {
                return false;
            }

            if (protocol == BusProtocol.VPW)
            {
                await this.SetBusFilters(BusFilters.All);
            }
            else if (protocol == BusProtocol.Can500k)
            {
                // CAN filters by acceptance id rather than by a header byte, so the common filter
                // API has nothing to say here: open the acceptance mask instead.
                //
                // Mask first. An acceptance id of zero against the exact-match mask still in place
                // accepts nothing at all, so the other order leaves the device deaf in between -
                // and permanently so if anything interrupts the pair.
                await SendCanConfig(AVT_CAN0_MASK0_ANY.GetBytes(), "CAN0 mask0 any 11-bit id");
                await SendCanConfig(AVT_CAN0_ACCEPT_ANY.GetBytes(), "CAN0 accept any id");
            }

            return true;
        }

        public override async Task EndMonitor()
        {
            if (this.CurrentProtocol == BusProtocol.VPW)
            {
                await this.RestoreBusFilters();
            }
            else if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                await SendCanConfig(BuildAcceptIdCommand(this.RxCanId), $"CAN0 accept ID {this.RxCanId:X3}");
                await SendCanConfig(AVT_CAN0_MASK0_EXACT.GetBytes(), "CAN0 mask0 exact-match");
            }
        }

        /// <summary>
        /// Install receive filters, using the destination match byte (AVT-85x manual 11.3).
        /// </summary>
        /// <remarks>
        /// The firmware matches on one destination byte, where zero means don't care. So a set
        /// naming a single destination becomes that byte, and anything wider clears the filter and
        /// lets the software sort out the extra traffic.
        ///
        /// There is a source match byte alongside it (5C), AND'ed with this one. Nothing here sets
        /// it, so it stays at its don't-care default.
        /// </remarks>
        public override async Task<bool> SetBusFilters(IReadOnlyList<BusFilter> filters)
        {
            // Null once disposed, and this runs on paths that can outlive the window closing.
            if (this.CurrentProtocol != BusProtocol.VPW || this.Port == null)
            {
                return false;
            }

            byte destination = 0x00;
            if (BusFilters.TryGetDestinations(filters, out IReadOnlyList<byte> destinations) &&
                destinations.Count == 1)
            {
                destination = destinations[0];
            }

            await this.Port.Send(new byte[] { 0x52, 0x5B, destination });

            Response<Message> confirmation =
                await this.FindResponse(new Message(new byte[] { 0x5B, destination }));

            if (confirmation.Status != ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage($"AVT filter: {destination:X2} was not accepted.");
                return false;
            }

            this.Logger.AddDebugMessage(destination == 0x00
                ? "AVT filter: off, so all traffic is delivered"
                : $"AVT filter: destination {destination:X2}");

            return true;
        }

        public override Task<bool> RestoreBusFilters()
        {
            return this.CurrentProtocol == BusProtocol.VPW
                ? this.SetBusFilters(BusFilters.VPW)
                : Task.FromResult(true);
        }

        /// <summary>
        /// Select the bus protocol the device communicates on. For CAN the AVT enters CAN mode and
        /// is configured for 500 kbaud raw frames; software ISO-TP (IsoTpTransport) does the
        /// segmentation and reassembly.
        /// </summary>
        public override async Task<bool> SetProtocol(BusProtocol protocol)
        {
            if (protocol == this.CurrentProtocol)
            {
                return true;
            }

            if (protocol == BusProtocol.Can500k)
            {
                // The 838 is VPW-only hardware; only the 842 and 852 have a CAN transceiver. Fail with
                // a clear reason rather than sending a CAN command the 838 cannot honour and relying on
                // it to reject it.
                if (this.Model == 838)
                {
                    this.Logger.AddUserMessage("The AVT 838 is VPW only and cannot be used with CAN-bus PCMs; an AVT 842 or 852 is required.");
                    return false;
                }

                await this.Port.DiscardBuffers();
                await this.Port.Send(AVT_ENTER_CAN_MODE.GetBytes());
                Response<Message> m = await ReadAVTPacket();
                if (m.Status != ResponseStatus.Success)
                {
                    this.Logger.AddUserMessage("AVT: unable to enter CAN mode.");
                    return false;
                }

                this.CurrentProtocol = BusProtocol.Can500k;

                // The config-command acknowledgements (8x ...) don't match the VPW packet shapes
                // ReadAVTPacket parses, so SendCanConfig drains them rather than parsing.
                await SendCanConfig(AVT_CAN0_500K.GetBytes(),            "CAN0 500 kbaud");
                await SendCanConfig(AVT_CAN0_IDMASK_MODE4.GetBytes(),    "CAN0 ID/Mask mode 4");
                await SendCanConfig(BuildAcceptIdCommand(this.RxCanId),  $"CAN0 accept ID {this.RxCanId:X3}");
                await SendCanConfig(AVT_CAN0_MASK0_EXACT.GetBytes(),     "CAN0 mask0 exact-match");
                await SendCanConfig(AVT_CAN0_ISO15765_OFF.GetBytes(),    "CAN0 ISO 15765 off (raw frames)");
                await SendCanConfig(AVT_ENABLE_CAN0.GetBytes(),          "CAN0 enable");

                this.Supports4X = false;
                this.Logger.AddDebugMessage($"AVT CAN0 ready: 500k, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}, software ISO-TP.");
                return true;
            }

            if (protocol == BusProtocol.VPW)
            {
                if (this.Model != 838)
                {
                    await this.Port.Send(AVT_ENTER_VPW_MODE.GetBytes());
                    Response<Message> m = await FindResponse(AVT_VPW);
                    if (m.Status != ResponseStatus.Success)
                    {
                        this.Logger.AddUserMessage("AVT: unable to re-enter VPW mode.");
                        return false;
                    }
                }

                // Restore the VPW flag before AVTSetup so its packet reads use VPW framing.
                this.CurrentProtocol = BusProtocol.VPW;
                await AVTSetup();
                this.Supports4X = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Build the AVT "set CAN0 acceptance ID0" command (75 2A 00 00 idHi idLo) for an 11-bit ID,
        /// right-justified in 16 bits (e.g. 0x7E8 -> 07 E8).
        /// </summary>
        private static byte[] BuildAcceptIdCommand(uint canId)
        {
            ushort id = (ushort)(canId & 0x7FF);
            return new byte[] { 0x75, 0x2A, 0x00, 0x00, (byte)(id >> 8), (byte)id };
        }

        /// <summary>
        /// Send an AVT CAN configuration command and drain its acknowledgement (an 8x echo that
        /// doesn't fit the VPW packet shapes ReadAVTPacket parses), consuming bytes until the line
        /// goes quiet so the receive stream stays aligned.
        /// </summary>
        private async Task SendCanConfig(byte[] command, string description)
        {
            // Null once the device has been disposed - closing the window during a configuration
            // sequence, for instance, which is long enough to be interrupted.
            IPort port = this.Port;
            if (port == null)
            {
                return;
            }

            await port.Send(command);

            byte[] scratch = new byte[1];
            Stopwatch idle = Stopwatch.StartNew();
            Stopwatch total = Stopwatch.StartNew();

            // Bounded on the gap since the last byte and on the whole drain. Waiting for the line
            // to go quiet is enough while configuring a bus nobody is listening to yet, but this
            // also runs when monitoring is switched off, with frames still arriving - and then the
            // line never goes quiet and the idle timer restarts for ever.
            while (idle.ElapsedMilliseconds < 150 && total.ElapsedMilliseconds < 1000)
            {
                if (await port.GetReceiveQueueSize() > 0)
                {
                    await port.Receive(scratch, 0, 1);
                    idle.Restart();
                }
            }

            this.Logger.AddDebugMessage("AVT CAN config: " + description);
        }

        /// <summary>
        /// Transmit one raw CAN frame via the AVT. Command content is [channel][id_hi][id_lo][data];
        /// channel 0x00 selects CAN0 raw-frame mode. SendAVTPacket prepends the AVT length prefix.
        /// </summary>
        public async Task SendCanFrame(uint canId, byte[] framePayload)
        {
            byte[] packet = new byte[3 + framePayload.Length];
            packet[0] = 0x00;
            packet[1] = (byte)(canId >> 8);
            packet[2] = (byte)canId;
            Buffer.BlockCopy(framePayload, 0, packet, 3, framePayload.Length);
            await SendAVTPacket(new Message(packet));
        }

        /// <summary>
        /// Read one raw CAN frame from the AVT. Receive format (timestamps off) is
        /// [flags|channel][id_hi][id_lo][data]. Returns (0, empty) on timeout.
        /// </summary>
        /// <remarks>
        /// Unlike the VPW path (ReadAVTPacket, one packet per call with a serial read per field), this
        /// drains the whole serial buffer in one read into <see cref="canRxBuffer"/> and slices frames
        /// out of memory. A streaming CAN read is hundreds of thousands of frames, so paying the serial
        /// round-trip once per burst instead of ~twice per frame is what makes the read fast.
        /// </remarks>
        public async Task<(uint id, byte[] frame)> ReceiveCanFrame()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                // Hand back any whole frame already buffered before touching the port again.
                if (TryDequeueCanFrame(out uint id, out byte[] frame))
                {
                    return (id, frame);
                }

                if (stopwatch.ElapsedMilliseconds >= this.receiveTimeoutMs)
                {
                    return (0u, Array.Empty<byte>());
                }

                int available = await this.Port.GetReceiveQueueSize();
                if (available > 0)
                {
                    byte[] buffer = new byte[available];
                    int read = await this.Port.Receive(buffer, 0, available);
                    for (int i = 0; i < read; i++)
                    {
                        this.canRxBuffer.Add(buffer[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Pull one whole CAN data frame from the front of <see cref="canRxBuffer"/>. In CAN mode a
        /// data frame is the short form 0x0L followed by L bytes of [flags|channel][id_hi][id_lo][data],
        /// so a single- and multi-byte frame (including a Flow Control) are the same shape, just a
        /// different L. Non-data packets (acks, notifications, runts) are dropped. Returns false when
        /// the buffer does not yet hold a complete packet.
        /// </summary>
        private bool TryDequeueCanFrame(out uint id, out byte[] frame)
        {
            id = 0u;
            frame = Array.Empty<byte>();

            while (this.canRxBuffer.Count > 0)
            {
                if (!TryDecodePacketLength(out int headerLength, out int payloadLength, out bool isData))
                {
                    // The length prefix is present but the full payload has not arrived yet.
                    return false;
                }

                int total = headerLength + payloadLength;
                if (this.canRxBuffer.Count < total)
                {
                    return false;
                }

                // A CAN frame needs at least flags + id_hi + id_lo. In CAN mode there is no VPW status
                // byte, so the payload is the CAN frame verbatim.
                if (isData && payloadLength >= 3)
                {
                    id = (uint)(((this.canRxBuffer[headerLength + 1] << 8) | this.canRxBuffer[headerLength + 2]) & 0x7FF);
                    int frameLength = payloadLength - 3;
                    frame = new byte[frameLength];
                    for (int i = 0; i < frameLength; i++)
                    {
                        frame[i] = this.canRxBuffer[headerLength + 3 + i];
                    }
                    this.canRxBuffer.RemoveRange(0, total);
                    return true;
                }

                // Ack / notification / too-short to be a frame: consume it and look at the next packet.
                this.canRxBuffer.RemoveRange(0, total);
            }

            return false;
        }

        /// <summary>
        /// Decode the AVT length prefix at the front of <see cref="canRxBuffer"/>, mirroring the framing
        /// rules in ReadAVTPacket. Reports the header byte count, the payload byte count, and whether the
        /// packet carries data. Returns false when too few bytes are buffered to know the full length.
        /// </summary>
        private bool TryDecodePacketLength(out int headerLength, out int payloadLength, out bool isData)
        {
            headerLength = 0;
            payloadLength = 0;
            isData = false;

            byte first = this.canRxBuffer[0];
            switch (first)
            {
                case 0x11: // length in the next byte
                    if (this.canRxBuffer.Count < 2) return false;
                    headerLength = 2;
                    payloadLength = this.canRxBuffer[1];
                    isData = true;
                    return true;

                case 0x12: // length in the next two bytes
                    if (this.canRxBuffer.Count < 3) return false;
                    headerLength = 3;
                    payloadLength = (this.canRxBuffer[1] << 8) | this.canRxBuffer[2];
                    isData = true;
                    return true;

                default:
                    int type = first >> 4;
                    int low = first & 0x0F;
                    headerLength = 1;
                    switch (type)
                    {
                        case 0x0: // standard short data packet (842/852)
                        case 0xF: // standard short data packet (838)
                            payloadLength = low;
                            isData = true;
                            break;

                        case 0x8: // high-speed notification: the low nibble counts one extra
                            payloadLength = low - 1;
                            break;

                        default: // 0x2/0x3/0x6/0x9/0xC acks and notifications
                            payloadLength = low;
                            break;
                    }

                    if (payloadLength < 0) payloadLength = 0;
                    return true;
            }
        }
    }
}

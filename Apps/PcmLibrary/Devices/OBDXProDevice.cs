// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// This class encapsulates all code that is unique to the DVI interface.
    /// </summary>
    /// 
    public class OBDXProDevice : SerialDevice, ICanTarget, ICanChannel
    {
        //Supported tools:
        //OBDX Pro VT v3- VPW only with USB, Wifi, BT, BLE
        //OBDX Pro VC - VPW only with USB
        //OBDX Pro GT - VPW with USB, Wifi, BT, BLE
        public string ToolConnected = "";
        public const string DeviceType = "OBDX Pro";
        public bool TimeStampsEnabled = false;
        public bool CRCInReceivedFrame = false;

        // This default is probably excessive but it should always be
        // overwritten by a call to SetTimeout before use anyhow.
        private VpwSpeed vpwSpeed = VpwSpeed.Standard;

        /// <summary>Bytes to strip from the front of each received network frame (4 = CAN ID prefix in CAN mode).</summary>
        protected int CanIdPrefixLength = 0;

        /// <summary>Current bus protocol; drives send/receive formatting for this device.</summary>
        protected BusProtocol CurrentProtocol { get; private set; } = BusProtocol.Vpw;

        /// <summary>True if the connected tool reports HS CAN support.</summary>
        protected bool CanSupported = false;

        // CAN target addresses. Default to the standard OBD2 PCM IDs (from the shared CanId
        // constants), but are settable so the command layer can address a different module or ID.
        /// <summary>CAN ID used when transmitting (tool to target).</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN ID accepted when receiving (target to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        /// <summary>No-progress wait the ISO-TP transport applies on the software-ISO-TP path.</summary>
        public int ReceiveTimeoutMilliseconds => this.GetReceiveTimeout();

        // Build-time choice of how CAN ISO-TP is done on this device. Not a UI/runtime setting -
        // flip this one constant and rebuild.
        //   false = NATIVE/hardware ISO-TP: the device firmware reassembles and handles flow control
        //           (a FLOW filter). More efficient; this is the default.
        //   true  = SOFTWARE ISO-TP: a PASS filter delivers raw CAN frames and IsoTpTransport does
        //           the framing. Use for testing, or as a workaround if a firmware ISO-TP bug
        //           resurfaces (some firmware leaves a stray ISO-TP pad byte on reassembled blocks).
        // Either way the device still filters in hardware to the target's response CAN id.
        private static readonly bool UseSoftwareIsoTpForCan = false;

        private readonly IsoTpTransport isoTp;
        private bool softwareIsoTp = false;

        // Raw CAN frames read off the serial stream while in software ISO-TP mode, awaiting the
        // transport. Buffered (not enqueued as device messages) so a frame that arrives during a
        // send's TX-ack wait is not lost to the receive path.
        private readonly Queue<(uint id, byte[] data)> pendingRawCanFrames = new Queue<(uint id, byte[] data)>();

        //ELM Command Set
        //To be put in, not really needed if using DVI command set

        //DVI (Direct Vehicle Interface) Command Set
        public static readonly Message DVI_BOARD_HARDWARE_VERSION = new Message(new byte[] { 0x22, 0x1, 0x0, 0 });
        public static readonly Message DVI_BOARD_FIRMWARE_VERSION = new Message(new byte[] { 0x22, 0x1, 0x1, 0 });
        public static readonly Message DVI_BOARD_MODEL = new Message(new byte[] { 0x22, 0x1, 0x2, 0 });
        public static readonly Message DVI_BOARD_NAME = new Message(new byte[] { 0x22, 0x1, 0x3, 0 });
        public static readonly Message DVI_UniqueSerial = new Message(new byte[] { 0x22, 0x1, 0x4, 0 });
        public static readonly Message DVI_Supported_OBD_Protocols = new Message(new byte[] { 0x22, 0x1, 0x5, 0 });
        public static readonly Message DVI_Supported_PC_Protocols = new Message(new byte[] { 0x22, 0x1, 0x6, 0 });

        public static readonly Message DVI_Req_NewtorkWriteStatus = new Message(new byte[] { 0x24, 0x1, 0x1, 0 });
        public static readonly Message DVI_Set_NewtorkWriteStatus = new Message(new byte[] { 0x24, 0x2, 0x1, 0, 0 });
        public static readonly Message DVI_Req_ConfigRespStatus = new Message(new byte[] { 0x24, 0x1, 0x2, 0 });
        public static readonly Message DVI_Set_CofigRespStatus = new Message(new byte[] { 0x24, 0x2, 0x2, 0, 0 });

        public static readonly Message DVI_Req_TimeStampOnRxNetwork = new Message(new byte[] { 0x24, 0x1, 0x3, 0 });
        public static readonly Message DVI_Set_TimeStampOnRxNetwork = new Message(new byte[] { 0x24, 0x2, 0x3, 0, 0 });

        public static readonly Message DVI_RESET = new Message(new byte[] { 0x25, 0x0, 0 });

        public static readonly Message DVI_Req_OBD_Protocol = new Message(new byte[] { 0x31, 0x1, 0x1, 0 });
        public static readonly Message DVI_Set_OBD_Protocol = new Message(new byte[] { 0x31, 0x2, 0x1, 0, 0 });
        public static readonly Message DVI_Req_NewtorkEnable = new Message(new byte[] { 0x31, 0x1, 0x2, 0 });
        public static readonly Message DVI_Set_NewtorkEnable = new Message(new byte[] { 0x31, 0x2, 0x2, 0, 0 });
        public static readonly Message DVI_Set_API_Protocol = new Message(new byte[] { 0x31, 0x2, 0x6, 0, 0 });

        public static readonly Message DVI_Req_To_Filter = new Message(new byte[] { 0x33, 0x1, 0x0, 0 });
        public static readonly Message DVI_Set_To_Filter = new Message(new byte[] { 0x33, 0x3, 0x0, 0, 0, 0 });
        public static readonly Message DVI_Req_From_Filter = new Message(new byte[] { 0x33, 0x1, 0x1, 0 });
        public static readonly Message DVI_Set_From_Filter = new Message(new byte[] { 0x33, 0x3, 0x1, 0, 0, 0 });
        public static readonly Message DVI_Req_RangeTo_Filter = new Message(new byte[] { 0x33, 0x1, 0x2, 0 });
        public static readonly Message DVI_Set_RangeTo_Filter = new Message(new byte[] { 0x33, 0x3, 0x2, 0, 0, 0, 0 });
        public static readonly Message DVI_Req_RangeFrom_Filter = new Message(new byte[] { 0x33, 0x1, 0x3, 0 });
        public static readonly Message DVI_Set_RangeFrom_Filter = new Message(new byte[] { 0x33, 0x4, 0x3, 0, 0, 0, 0 });
        public static readonly Message DVI_Req_Mask = new Message(new byte[] { 0x33, 0x1, 0x4, 0 });
        public static readonly Message DVI_Set_Mask = new Message(new byte[] { 0x33, 0x5, 0x4, 0, 0, 0, 0, 0 });
        public static readonly Message DVI_Req_Speed = new Message(new byte[] { 0x33, 0x1, 0x6, 0 });
        public static readonly Message DVI_Set_Speed = new Message(new byte[] { 0x33, 0x2, 0x6, 0, 0 });
        public static readonly Message DVI_Req_ValidateCRC_onRX = new Message(new byte[] { 0x33, 0x1, 0x7, 0 });
        public static readonly Message DVI_Set_ValidateCRC_onRX = new Message(new byte[] { 0x33, 0x2, 0x7, 0, 0 });
        public static readonly Message DVI_Req_Show_CRC_OnNetwork = new Message(new byte[] { 0x33, 0x1, 0x8, 0 });
        public static readonly Message DVI_Set_Show_CRC_OnNetwork = new Message(new byte[] { 0x33, 0x2, 0x8, 0, 0 });
        public static readonly Message DVI_Req_Write_Idle_Timeout = new Message(new byte[] { 0x33, 0x1, 0x9, 0 });
        public static readonly Message DVI_Set_Write_Idle_Timeout = new Message(new byte[] { 0x33, 0x3, 0x9, 0, 0, 0 });
        public static readonly Message DVI_Req_1x_Timings = new Message(new byte[] { 0x33, 0x2, 0xA, 0, 0 });
        public static readonly Message DVI_Set_1x_Timings = new Message(new byte[] { 0x33, 0x4, 0xA, 0, 0, 0, 0 });
        public static readonly Message DVI_Req_4x_Timings = new Message(new byte[] { 0x33, 0x2, 0xB, 0, 0 });
        public static readonly Message DVI_Set_4x_Timings = new Message(new byte[] { 0x33, 0x4, 0xB, 0, 0, 0, 0 });
        public static readonly Message DVI_Req_ResetTimings = new Message(new byte[] { 0x33, 0x2, 0xC, 0, 0 });
        public static readonly Message DVI_Req_ErrorBits = new Message(new byte[] { 0x33, 0x1, 0xD, 0 });
        public static readonly Message DVI_Set_ErrorBits = new Message(new byte[] { 0x33, 0x3, 0xD, 0, 0, 0 });
        public static readonly Message DVI_Req_ErrorCount = new Message(new byte[] { 0x33, 0x2, 0xE, 0, 0 });
        public static readonly Message DVI_Req_DefaultSettings = new Message(new byte[] { 0x33, 0x1, 0xF, 0 });

        public static readonly Message DVI_Req_ADC_SingleChannel = new Message(new byte[] { 0x35, 0x2, 0x0, 0, 0 });
        public static readonly Message DVI_Req_ADC_MultipleChannels = new Message(new byte[] { 0x35, 0x2, 0x1, 0, 0 });

        public OBDXProDevice(IPort port, ILogger logger) : base(port, logger)
        {
            this.isoTp = new IsoTpTransport(this);
            this.MaxSendSize = 4096 + 10 + 2;    // packets up to 4112 but we want 4096 byte data blocks
            this.MaxReceiveSize = 4096 + 10 + 2; // with 10 byte header and 2 byte block checksum
            this.Supports4X = true;
            this.SupportsSingleDpidLogging = true;
            this.SupportsStreamLogging = true;

            // This will be used during device initialization.
            this.currentTimeoutScenario = TimeoutScenario.ReadProperty;
        }

        public override string GetDeviceType()
        {
            return ToolConnected == "" ? DeviceType : ToolConnected;
        }

        public override async Task<bool> Initialize()
        {
            this.Logger.AddDebugMessage("Initializing " + this.ToString());


            SerialPortConfiguration configuration = new SerialPortConfiguration();
            configuration.BaudRate = 115200;
            configuration.Timeout = 1000;
            await this.Port.OpenAsync(configuration);
            System.Threading.Thread.Sleep(200);

            //Reset scantool - ensures starts at ELM protocol
            bool Status = await ResetDevice();
            if (Status == false)
            {
                this.Logger.AddUserMessage("Unable to reset DVI device.");
                return false;
            }

             

            //Request Board information
            Response<string> BoardName = await GetBoardDetails();
            if (BoardName.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("Unable to get DVI device details.");
                return false;
            }

            //Detect HS CAN support
            this.CanSupported = await QueryCanSupported();
            this.Logger.AddDebugMessage("HS CAN supported: " + this.CanSupported);


            //Read voltage
            Response<double> ReadVoltageVal = await ReadVoltage();
            if (ReadVoltageVal.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("Unable to read voltage.");
                return false;
            }
            this.Logger.AddUserMessage("Voltage is: " + ReadVoltageVal.Value.ToString("F2") + "V");


            //Set protocol to VPW mode
            Status = await SetProtocol(OBDProtocols.VPW);
            if (Status == false)
            {
                this.Logger.AddUserMessage("Unable to set DVI device protocol to VPW.");
                return false;
            }

            Response<bool> SetupStatus = await DVISetup();
            if (SetupStatus.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("DVI device initialization failed.");
                return false;
            }

            this.Logger.AddUserMessage("Device Successfully Initialized and Ready");
            return true;
        }

        /// <summary>
        /// Not yet implemented.
        /// </summary>
        public override Task<TimeoutScenario> SetTimeout(TimeoutScenario scenario)
        {
            TimeoutScenario previousScenario = this.currentTimeoutScenario;
            this.currentTimeoutScenario = scenario;
            return Task.FromResult(previousScenario);
        }

        /// <summary>
        /// This will process incoming messages for up to 250ms looking for a message
        /// </summary>

        public async Task<Response<Message>> FindResponseFromTool(byte[] expected)
        {
            //this.Logger.AddDebugMessage("FindResponse called");
            for (int iterations = 0; iterations < 5; iterations++)
            {
                Response<Message> response = await this.ReadDVIPacket(this.GetReceiveTimeout());
                if (response != null)  // Hack to silence error - See: https://pcmhacking.net/forums/viewtopic.php?f=42&t=6730&start=110#p101790
                    if (response.Status == ResponseStatus.Success)
                        if (Utility.CompareArraysPart(response.Value.GetBytes(), expected))
                            return Response.Create(ResponseStatus.Success, (Message)response.Value);
                await Task.Delay(50);
            }

            return Response.Create(ResponseStatus.Timeout, (Message)null!);
        }

        /// <summary>
        /// Wait for serial byte to be availble. False if timeout.
        /// </summary>
        async private Task<bool> WaitForSerial(ushort NumBytes, int timeout = 0)
        {
            if (timeout == 0)
            {
                timeout = 1000;
            }

            int TempCount = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            // Wait for bytes to arrive...
            while (sw.ElapsedMilliseconds < timeout)
            {
                var rqSize = await this.Port.GetReceiveQueueSize();

                if (rqSize > TempCount)
                {
                    TempCount = rqSize;
                    sw.Restart();
                }

                if (rqSize >= NumBytes) 
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return false;
        }

        /// <summary>
        /// Read an DVI formatted packet from the interface.
        /// If it recevies a Network message, in enqueues it and returns null;
        /// If it receives a Device message, it returns the message.
        /// </summary>
        async private Task<Response<Message>> ReadDVIPacket(int timeout = 0)
        {
            UInt16 Length = 0;

            byte offset = 0;
            byte[] rx = new byte[3]; // we dont read more than 3 bytes at a time
            byte[] timestampbuf = new byte[3];
            ulong timestampmicro = 0;
            // First Byte is command
            //Second is length, third also for long frame
            //Data
            //Checksum
            bool Chk = false;
            try
            {
                Chk = (await WaitForSerial(1, timeout));
                if (Chk == false)
                {
                    this.Logger.AddDebugMessage("Timeout.. no data present A");
                    return Response.Create(ResponseStatus.Timeout, (Message)null!);
                }

                //get first byte for command
                await this.Port.Receive(rx, 0, 1);
            }
            catch (Exception) // timeout exception - log no data, return error.
            {
                this.Logger.AddDebugMessage("No Data");
                return Response.Create(ResponseStatus.Timeout, (Message)null!);
            }


            if (rx[0] == 0x8 || rx[0] == 0x9) //for network frames
            {
                //check if timestamps enabled
                if (TimeStampsEnabled)
                {
                    //next 4 bytes will be timestamp in microseconds
                    for (byte i = 0; i < 4; i++)
                    {
                        Chk = (await WaitForSerial(1));
                        if (Chk == false)
                        {
                            this.Logger.AddDebugMessage("Timeout.. no data present B");
                            return Response.Create(ResponseStatus.Timeout, (Message)null!);
                        }
                        await this.Port.Receive(timestampbuf, i, 1);
                    }
                    timestampmicro = (ulong)((ulong)timestampbuf[0] * 0x100 ^ 3) + (ulong)((ulong)timestampbuf[1] * 0x100 ^ 2) + (ulong)((ulong)timestampbuf[0] * 0x100) + (ulong)timestampbuf[0];
                }
                if (rx[0] == 0x8) //if short, only get one byte for length
                {
                    Chk = (await WaitForSerial(1));
                    if (Chk == false)
                    {
                        this.Logger.AddDebugMessage("Timeout.. no data present C");
                        return Response.Create(ResponseStatus.Timeout, (Message)null!);
                    }
                    await this.Port.Receive(rx, 1, 1);
                    Length = rx[1];
                }
                else //if long, get two bytes for length
                {
                    offset += 1;
                    Chk = (await WaitForSerial(2));
                    if (Chk == false)
                    {
                        this.Logger.AddDebugMessage("Timeout.. no data present D");
                        return Response.Create(ResponseStatus.Timeout, (Message)null!);
                    }
                    await this.Port.Receive(rx, 1, 2);
                    Length = (ushort)((ushort)(rx[1] * 0x100) + rx[2]);
                }

            }
            else //for all other received frames
            {
                Chk = (await WaitForSerial(1));
                if (Chk == false)
                {
                    this.Logger.AddDebugMessage("Timeout.. no data present E");
                    return Response.Create(ResponseStatus.Timeout, (Message)null!);
                }
                await this.Port.Receive(rx, 1, 1);
                Length = rx[1];
            }

            byte[] receive = new byte[Length + 3 + offset];
            Chk = (await WaitForSerial((ushort)(Length + 1)));
            if (Chk == false)
            {
                this.Logger.AddDebugMessage("Timeout.. no data present F");
                return Response.Create(ResponseStatus.Timeout, (Message)null!);
            }

            int bytes;
            receive[0] = rx[0];//Command
            receive[1] = rx[1];//length
            if (rx[0] == 0x09) receive[2] = rx[2];//length long frame
            bytes = await this.Port.Receive(receive, 2 + offset, Length + 1);//get rest of frame
            if (bytes <= 0)
            {
                this.Logger.AddDebugMessage("Failed reading " + Length + " byte packet");
                return Response.Create(ResponseStatus.Error, (Message)null!);
            }
            //should have entire frame now
            //verify checksum correct
            byte CalcChksm = 0;
            for (ushort i = 0; i < (receive.Length - 1); i++) CalcChksm += receive[i];
            if (rx[0] == 0x08 || rx[0] == 0x09)
            {
                if (TimeStampsEnabled)
                {
                    CalcChksm += timestampbuf[0];
                    CalcChksm += timestampbuf[1];
                    CalcChksm += timestampbuf[2];
                    CalcChksm += timestampbuf[3];
                }
            }
            CalcChksm = (byte)~CalcChksm;

            if (receive[receive.Length - 1] != CalcChksm)
            {
                this.Logger.AddDebugMessage("Total Length Data=" + Length + " RX: " + receive.ToHex());
                this.Logger.AddDebugMessage("Checksum error on received message.");
                return Response.Create(ResponseStatus.Error, (Message)null!);
            }

            // this.Logger.AddDebugMessage("Total Length Data=" + Length + " RX: " + receive.ToHex());

            if (receive[0] == 0x8 || receive[0] == 0x9)
            {
                //network frames //Strip header and checksum
                byte[] StrippedFrame = new byte[Length];
                Buffer.BlockCopy(receive, 2 + offset, StrippedFrame, 0, Length);

                // Software ISO-TP: each network frame is one raw CAN frame [4-byte id][data]. Buffer
                // (id, data) for ReceiveCanFrame rather than stripping/enqueuing; the transport
                // reassembles. Return UnexpectedResponse so a send's TX-ack wait keeps looking.
                if (this.softwareIsoTp)
                {
                    if (StrippedFrame.Length >= 4)
                    {
                        uint id = (uint)((StrippedFrame[0] << 24) | (StrippedFrame[1] << 16) | (StrippedFrame[2] << 8) | StrippedFrame[3]);
                        byte[] data = new byte[StrippedFrame.Length - 4];
                        Buffer.BlockCopy(StrippedFrame, 4, data, 0, data.Length);
                        this.pendingRawCanFrames.Enqueue((id, data));
                    }
                    return Response.Create(ResponseStatus.UnexpectedResponse, (Message)null!);
                }

                // In CAN mode each network frame is prefixed with the 4-byte CAN ID; strip it so
                // the rest of the stack sees the bare UDS/GMLAN payload (the device's native ISO-TP
                // has already reassembled a whole payload for us).
                byte[] frameToEnqueue = StrippedFrame;
                if (this.CanIdPrefixLength > 0 && StrippedFrame.Length > this.CanIdPrefixLength)
                {
                    frameToEnqueue = new byte[StrippedFrame.Length - this.CanIdPrefixLength];
                    Buffer.BlockCopy(StrippedFrame, this.CanIdPrefixLength, frameToEnqueue, 0, frameToEnqueue.Length);
                }

                // The device delivers the ISO-TP header along with the payload: a single frame as
                // [0L][payload][padding to 8], and a reassembled multi-frame message as
                // [1L][LL][payload]. Strip the header and keep exactly the declared length, which
                // also drops the frame padding a short reply carries.
                if (this.CurrentProtocol == BusProtocol.Can500k && frameToEnqueue.Length >= 2)
                {
                    int declaredLength = 0;
                    int headerLength = 0;

                    if ((frameToEnqueue[0] & 0xF0) == 0x00 && frameToEnqueue[0] > 0)
                    {
                        declaredLength = frameToEnqueue[0];
                        headerLength = 1;
                    }
                    else if ((frameToEnqueue[0] & 0xF0) == 0x10)
                    {
                        declaredLength = ((frameToEnqueue[0] & 0x0F) << 8) | frameToEnqueue[1];
                        headerLength = 2;
                    }

                    if (declaredLength > 0 && frameToEnqueue.Length >= headerLength + declaredLength)
                    {
                        byte[] unwrapped = new byte[declaredLength];
                        Buffer.BlockCopy(frameToEnqueue, headerLength, unwrapped, 0, declaredLength);
                        frameToEnqueue = unwrapped;
                    }
                }

                // Native ISO-TP: report the reassembled CAN payload once, with its id; VPW keeps the
                // generic "Received:" line. Off-conversation frames (filtered out) are not logged.
                if (this.CurrentProtocol == BusProtocol.Can500k)
                {
                    if (this.Enqueue(new Message(frameToEnqueue, timestampmicro, 0), logReceived: false))
                    {
                        this.Logger.AddDebugMessage($"RX: {this.RxCanId:X3} {frameToEnqueue.ToHex()}");
                    }
                }
                else
                {
                    this.Enqueue(new Message(frameToEnqueue, timestampmicro, 0));
                }
                return Response.Create(ResponseStatus.UnexpectedResponse, (Message)null!);
            }
            else if (receive[0] == 0x7F)
            {
                // Error from the device
                Message result = new Message(receive);
                this.Logger.AddDebugMessage("XPro Error: " + result.ToString());
                return Response.Create(ResponseStatus.Error, result);
            }
            else
            {
                // Valid message from the device
                this.Logger.AddDebugMessage("XPro: " + receive.ToHex());
                return Response.Create(ResponseStatus.Success, new Message(receive));
            }
        }


        async private Task<Response<String>> ReadELMPacket(String SentFrame)
        {
            // UInt16 Counter = 0;
            bool framefound = false;
            bool Chk = false;

            string StrResp = "";
            byte[] rx = { 0 };
            try
            {
                while (framefound == false)
                {
                    Chk = (await WaitForSerial(1));
                    if (Chk == false)
                    {
                        this.Logger.AddDebugMessage("Timeout.. no data present");
                        return Response.Create(ResponseStatus.Timeout, "");
                    }

                    await this.Port.Receive(rx, 0, 1);
                    if (rx[0] == 0xD) //carriage return
                    {
                        if (StrResp != SentFrame)
                        {
                            framefound = true;
                            break;
                        }
                        StrResp = "";
                        continue;
                    }
                    else if (rx[0] == 0xA) continue;//newline
                    StrResp += Convert.ToChar(rx[0]);
                }

                //Find Idle frame
                framefound = false;
                while (framefound == false)
                {
                    Chk = (await WaitForSerial(1));
                    if (Chk == false)
                    {
                        this.Logger.AddDebugMessage("ELM Idle frame not detected");
                        return Response.Create(ResponseStatus.Timeout, "");
                    }
                    await this.Port.Receive(rx, 0, 1);
                    if (rx[0] == '>')
                    {
                        framefound = true;
                        break;
                    }
                }
                return Response.Create(ResponseStatus.Success, StrResp);

            }
            catch (Exception) // timeout exception - log no data, return error.
            {
                this.Logger.AddDebugMessage("No Data");
                return Response.Create(ResponseStatus.Timeout, (""));
            }

        }

        public override async Task<byte?> ReadBroadcastState(byte command)
        {
            this.ClearMessageQueue();
            await this.ReadDVIPacket(200);
            Message incoming = await ReceiveMessage();
            return MatchBroadcast(incoming, command);
        }

        /// <summary>
        /// Calc checksum for byte array for all messages to/from device
        /// </summary>
        private byte CalcChecksum(byte[] MyArr)
        {
            byte CalcChksm = 0;
            for (ushort i = 0; i < (MyArr.Length - 1); i++)
            {
                CalcChksm += MyArr[i];
            }
            return ((byte)~CalcChksm);
        }


        /// <summary>
        /// Convert a Message to an DVI formatted transmit, and send to the interface
        /// </summary>
        async private Task<Response<Message>> SendDVIPacket(Message message, bool logTx = true)
        {
            int length = message.GetBytes().Length;
            byte[] RawPacket = message.GetBytes();
            byte[] SendPacket = new byte[length + 3];

            if (length > 0xFF)
            {
                System.Array.Resize(ref SendPacket, SendPacket.Length + 1);
                SendPacket[0] = 0x11;
                SendPacket[1] = (byte)(length >> 8);
                SendPacket[2] = (byte)length;
                Buffer.BlockCopy(RawPacket, 0, SendPacket, 3, length);
            }
            else
            {
                SendPacket[0] = 0x10;
                SendPacket[1] = (byte)length;
                Buffer.BlockCopy(RawPacket, 0, SendPacket, 2, length);
            }

            //Add checksum
            SendPacket[SendPacket.Length - 1] = CalcChecksum(SendPacket);

            //Send frame
            await this.Port.Send(SendPacket);

            // Wait for confirmation of successful send (the 0x20/0x21 TX acknowledgment).
            //
            // Received bus frames (0x08/0x09) arrive asynchronously on the same stream, so one
            // can land between our send and its acknowledgment. ReadDVIPacket has already
            // enqueued such a frame for the normal receive path and returned UnexpectedResponse;
            // that is not a send failure, so we keep reading until the real ack (or a fault)
            // arrives. Only genuine silence (Timeout) counts against the give-up budget.
            Response<Message>? m = null;
            int timeouts = 0;

            for (int reads = 0; reads < 64 && timeouts < 10; reads++)
            {
                m = await ReadDVIPacket(500);
                if (m == null)
                {
                    continue;
                }
                if (m.Status == ResponseStatus.Timeout)
                {
                    timeouts++;
                    continue;
                }
                if (m.Status == ResponseStatus.UnexpectedResponse)
                {
                    // Unsolicited inbound bus frame, already enqueued; keep waiting for the ack.
                    continue;
                }
                break;
            }

            if (m == null)
            {
                // This should never happen, but just in case...
                this.Logger.AddUserMessage("No response to send attempt. " + message.ToString());
                return Response.Create(ResponseStatus.Error, new Message(new byte[0]));
            }

            if (m.Status == ResponseStatus.Success)
            {
                byte[] Val = m.Value.GetBytes();
                if (Val[0] == 0x20 && Val[2] == 0x00)
                {
                    if (logTx) this.Logger.AddDebugMessage("TX: " + message.ToString());
                    return Response.Create(ResponseStatus.Success, message);
                }
                else if (Val[0] == 0x21 && Val[2] == 0x00)
                {
                    if (logTx) this.Logger.AddDebugMessage("TX: " + message.ToString());
                    return Response.Create(ResponseStatus.Success, message);
                }
                else
                {
                    this.Logger.AddUserMessage("Unable to transmit, odd response from device: " + message.ToString());
                    return Response.Create(ResponseStatus.Error, message);
                }
            }
            else
            {
                this.Logger.AddUserMessage("Unable to transmit, " + m.Status + ": " + message.ToString());
                return Response.Create(ResponseStatus.Error, message);
            }
        }

        /// <summary>
        /// Configure DVI to return only packets targeted to the tool (Device ID F0), and disable transmit acks
        /// </summary>
        async private Task<Response<Boolean>> DVISetup()
        {
            //Set filter
            bool Status = await SetToFilter(DeviceId.Tool);
            if (Status == false) return Response.Create(ResponseStatus.Error, false);

            //Enable network rx/tx for protocol
            Status = await EnableProtocolNetwork();
            if (Status == false) return Response.Create(ResponseStatus.Error, false);

            return Response.Create(ResponseStatus.Success, true);
        }

        async private Task<Response<double>> ReadVoltage()
        {
            byte[] Msg = new byte[] { 0x3A, 2, 0x0, (byte)0, 0 };
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            byte[] RespBytes = new byte[Msg.Length];
            Array.Copy(Msg, RespBytes, Msg.Length);
            RespBytes[0] += (byte)0x10;
            RespBytes[RespBytes.Length - 1] = CalcChecksum(RespBytes);
            Response<Message> response = await ReadDVIPacket();
            if (response.Status != ResponseStatus.Success)
            {
             //   this.Logger.AddDebugMessage("Network enabled");
                return Response.Create(response.Status, (double)0);
            }
            else
            {
                int RawADC = (int)((response.Value[4] * Math.Pow(0x100, 1)) + response.Value[5]);
                double COnvertedVoltage = ((((double)RawADC * 0.009047468) + 0.2)); //Should match for both VT and GT (Close enough).
                //this.Logger.AddDebugMessage("Voltage is: " + COnvertedVoltage.ToString("F2") + "V"); //2 decimal places
                return Response.Create(ResponseStatus.Success, COnvertedVoltage);
            }
        }

        /// <summary>
        /// Send a message, wait for a response, return the response.
        /// </summary>
        public override async Task<bool> SendMessage(Message message)
        {
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                // Log the whole payload once, whether the device or IsoTpTransport does the framing.
                byte[] uds = message.GetBytes();
                this.Logger.AddDebugMessage($"TX: {this.TxCanId:X3} {uds.ToHex()}");

                if (this.softwareIsoTp)
                {
                    // Software ISO-TP: the transport segments the payload and calls SendCanFrame per frame.
                    return await this.isoTp.SendMessage(message);
                }

                // Native ISO-TP: prepend the 4-byte destination CAN ID; the device adds the framing.
                byte[] withCanId = new byte[4 + uds.Length];
                withCanId[0] = (byte)(this.TxCanId >> 24);
                withCanId[1] = (byte)(this.TxCanId >> 16);
                withCanId[2] = (byte)(this.TxCanId >> 8);
                withCanId[3] = (byte)this.TxCanId;
                Buffer.BlockCopy(uds, 0, withCanId, 4, uds.Length);
                await SendDVIPacket(new Message(withCanId), logTx: false);
                return true;
            }

            await SendDVIPacket(message);
            return true;
        }

        /// <summary>
        /// Receive a message from the network - or at least try to.
        /// </summary>
        /// <remarks>
        /// Messages are placed into the queue by the code in ReadDvIPacket.
        /// Retry loops and message processing are in the application layer.
        /// </remarks>
        protected async override Task Receive()
        {
            if (this.CurrentProtocol == BusProtocol.Can500k && this.softwareIsoTp)
            {
                // Software ISO-TP reassembles a whole message from raw frames (via ReceiveCanFrame).
                // Keep reassembling until the active inbound filter accepts one (the reply we are
                // waiting for) or the bus goes quiet. Off-conversation traffic - e.g. a stale ack
                // the module repeats - is filtered out without being mistaken for silence, so a
                // burst of it can never starve the response we are actually waiting for.
                while (true)
                {
                    Message? assembled = await this.isoTp.ReceiveMessage();
                    if (assembled == null)
                    {
                        // Genuine transport timeout: nothing more on the wire right now.
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

            await ReadDVIPacket();
        }

        /// <summary>Send one raw CAN frame: DVI 0x10 with the 4-byte CAN id then the frame bytes.
        /// With write auto-format off the device transmits these bytes verbatim as the CAN payload.</summary>
        public async Task SendCanFrame(uint canId, byte[] framePayload)
        {
            byte[] withId = new byte[4 + framePayload.Length];
            withId[0] = (byte)(canId >> 24);
            withId[1] = (byte)(canId >> 16);
            withId[2] = (byte)(canId >> 8);
            withId[3] = (byte)canId;
            Buffer.BlockCopy(framePayload, 0, withId, 4, framePayload.Length);
            await SendDVIPacket(new Message(withId), logTx: false);
        }

        /// <summary>Read one raw CAN frame (id + data). Returns (0, empty) on a device read timeout.
        /// Frames read while waiting for a send's TX-ack are buffered, so none are lost.</summary>
        public async Task<(uint id, byte[] frame)> ReceiveCanFrame()
        {
            while (this.pendingRawCanFrames.Count == 0)
            {
                Response<Message> response = await ReadDVIPacket();
                if (response.Status == ResponseStatus.Timeout)
                {
                    return (0u, Array.Empty<byte>());
                }
                // A network frame (0x08/0x09) is buffered into pendingRawCanFrames by ReadDVIPacket;
                // any other response (e.g. a stray ack) is ignored and we read again.
            }

            (uint id, byte[] data) f = this.pendingRawCanFrames.Dequeue();
            return (f.id, f.data);
        }

        private async Task<bool> ResetDevice()
        {
            //Send DVI reset
            byte[] Msg = OBDXProDevice.DVI_RESET.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);
            System.Threading.Thread.Sleep(200);
            // await Task.Delay(200);
            await this.Port.DiscardBuffers();

            //Send ELM reset
            byte[] MsgATZ = { (byte)'A', (byte)'T', (byte)'Z', 0xD };
            await this.Port.Send(MsgATZ);
            System.Threading.Thread.Sleep(50);
            await this.Port.Send(MsgATZ);
            System.Threading.Thread.Sleep(400);
            await this.Port.DiscardBuffers();


            //AT@1 will return OBDX Pro VT - will then need to change its API to DVI bytes.
            byte[] MsgAT1 = { (byte)'A', (byte)'T', (byte)'@', (byte)'1', 0xD };
            await this.Port.Send(MsgAT1);
            Response<String> m = await ReadELMPacket("AT@1");
            if (m.Status == ResponseStatus.Success) this.Logger.AddUserMessage("Device Found: " + m.Value);
            else { this.Logger.AddUserMessage("OBDX Pro device not found or failed response"); return false; }

            System.Threading.Thread.Sleep(150);
            await this.Port.DiscardBuffers();

            //Change to DVI protocol DX 
            byte[] MsgDXDP = { (byte)'D', (byte)'X', (byte)'D', (byte)'P', (byte)'1', 0xD };
            await this.Port.Send(MsgDXDP);
            m = await ReadELMPacket("DXDP1");
            if (m.Status == ResponseStatus.Success && m.Value == "OK") this.Logger.AddDebugMessage("Switched to DVI protocol");
            else { this.Logger.AddUserMessage("Failed to switch to DVI protocol"); return false; }
            return true;
        }

        private async Task<Response<string>> GetBoardDetails()
        {
            string Details = "";
            byte[] Msg = OBDXProDevice.DVI_BOARD_NAME.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            Response<Message> m = await ReadDVIPacket();
            if (m.Status == ResponseStatus.Success)
            {
                byte[] Val = m.Value.GetBytes();
                ToolConnected = System.Text.Encoding.ASCII.GetString(Val, 3, Val[1] - 1);
                //  this.Logger.AddUserMessage("Device Found: " + name);
                // return new Response<String>(ResponseStatus.Success, name);
            }
            else
            {
                this.Logger.AddUserMessage("OBDX Pro device not found or failed response");
                return new Response<String>(ResponseStatus.Error, null!);
            }
            Details = ToolConnected;

            if (ToolConnected == "OBDX Pro VC") //Must reduce block size
            {
                this.MaxSendSize = 2048 + 10 + 2;    // 2048 byte data blocks with 10 byte 
                this.MaxReceiveSize = 2048 + 10 + 2; // header and 2 byte block checksum
            }

            //Firmware version
            Msg = OBDXProDevice.DVI_BOARD_FIRMWARE_VERSION.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);
            m = await ReadDVIPacket();
            if (m.Status == ResponseStatus.Success)
            {
                byte[] Val = m.Value.GetBytes();
                string Firmware = "";
                if (ToolConnected == "OBDX Pro VT")
                {
                    Firmware = ((float)(Val[3] * 0x100 + Val[4]) / 100).ToString("n2");
                }
                else //new firmware standard
                {
                    Firmware = Val[3].ToString() + "." + Val[4].ToString() + "." + Val[5].ToString() + "." + Val[6].ToString();
                }
               
                this.Logger.AddDebugMessage("Firmware version: v" + Firmware);
                Details += " - Firmware: v" + Firmware;
            }
            else
            {
                this.Logger.AddUserMessage("Unable to read firmware version");
                return new Response<String>(ResponseStatus.Error, null!);
            }

            //Hardware version
            Msg = OBDXProDevice.DVI_BOARD_HARDWARE_VERSION.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);
            m = await ReadDVIPacket();
            if (m.Status == ResponseStatus.Success)
            {
                byte[] Val = m.Value.GetBytes();
                string Hardware = "";
                if (ToolConnected == "OBDX Pro VT")
                {
                    Hardware = ((float)(Val[3] * 0x100 + Val[4]) / 100).ToString("n2");
                }
                else //new firmware standard
                {
                    Hardware = Val[3].ToString() + "." + Val[4].ToString() + "." + Val[5].ToString() + "." + Val[6].ToString();
                }
                 
                this.Logger.AddDebugMessage("Hardware version: v" + Hardware);
                Details += " - Hardware: v" + Hardware;
            }
            else
            {
                this.Logger.AddUserMessage("Unable to read hardware version");
                return new Response<String>(ResponseStatus.Error, null!);
            }


            //Unique Serial
            Msg = OBDXProDevice.DVI_UniqueSerial.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);
            m = await ReadDVIPacket();
            if (m.Status == ResponseStatus.Success)
            {
                byte[] Val = m.Value.GetBytes();
                byte[] serial = new byte[12];
                Array.Copy(Val, 3, serial, 0, 12);
                String Serial = string.Join("", Array.ConvertAll(serial, b => b.ToString("X2")));
                this.Logger.AddDebugMessage("Unique Serial: " + Serial);
                Details += " - Unique Serial: " + Serial;
                return new Response<String>(ResponseStatus.Success, Details);
            }
            else
            {
                this.Logger.AddUserMessage("Unable to read unique Serial");
                return new Response<String>(ResponseStatus.Error, null!);
            }
        }

        enum OBDProtocols : UInt16
        {
            VPW = 1,
            HSCAN = 2
        }
        private async Task<bool> SetProtocol(OBDProtocols val)
        {
            byte[] Msg = OBDXProDevice.DVI_Set_OBD_Protocol.GetBytes();
            Msg[3] = (byte)val;
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            //get response
            byte[] RespBytes = new byte[Msg.Length];
            Array.Copy(Msg, RespBytes, Msg.Length);
            RespBytes[0] += (byte)0x10;
            RespBytes[RespBytes.Length - 1] = CalcChecksum(RespBytes);
            Response<Message> m = await FindResponseFromTool(RespBytes);
            if (m.Status == ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("OBD Protocol set to " + val);
            }
            else
            {
                this.Logger.AddUserMessage("Unable to set OBDX Pro protocol to " + val);
                this.Logger.AddDebugMessage("Expected " + string.Join(" ", Array.ConvertAll(Msg, b => b.ToString("X2"))));
                return false;
            }
            return true;
        }

        /// <summary>VPW and CAN 500k can both be monitored on this device.</summary>
        public override IReadOnlyList<BusProtocol> MonitorableProtocols { get; } = new[] { BusProtocol.Vpw, BusProtocol.Can500k };

        /// <summary>
        /// Begin monitoring. The VPW "to tool" filter (set in DVISetup) only passes frames addressed to
        /// us (F0), so a passive monitor would see nothing; turn it off so all bus traffic comes through.
        /// </summary>
        public override async Task<bool> BeginMonitor(BusProtocol protocol)
        {
            if (!await this.SetProtocol(protocol))
            {
                return false;
            }

            if (protocol == BusProtocol.Vpw)
            {
                await this.SetToFilter(DeviceId.Tool, false);
            }

            return true;
        }

        public override async Task EndMonitor()
        {
            if (this.CurrentProtocol == BusProtocol.Vpw)
            {
                await this.SetToFilter(DeviceId.Tool, true);
            }
        }

        /// <summary>
        /// Select the bus protocol the device communicates on. The OBDX Pro GT (and CAN-capable VT)
        /// supports more than one on the same physical device. For CAN it switches the DVI protocol
        /// to HS CAN and configures ISO-TP per <see cref="UseSoftwareIsoTpForCan"/> - native (a FLOW
        /// filter; the device does reassembly/flow control) by default, or software (a PASS filter
        /// delivering raw frames that IsoTpTransport reassembles).
        /// </summary>
        public override async Task<bool> SetProtocol(BusProtocol protocol)
        {
            if (protocol == this.CurrentProtocol)
            {
                return true;
            }

            if (protocol == BusProtocol.Can500k)
            {
                if (!this.CanSupported)
                {
                    this.Logger.AddUserMessage("This OBDX Pro does not support CAN.");
                    return false;
                }

                // Switching protocol disables the network, so set protocol, configure CAN, then
                // re-enable. Native uses a FLOW filter (device does ISO-TP); software uses a PASS
                // filter (raw frames) plus write auto-format off, and IsoTpTransport does the framing.
                // Both filter in hardware to the target's rx id; see UseSoftwareIsoTpForCan.
                if (!await SetProtocol(OBDProtocols.HSCAN))
                {
                    return false;
                }

                if (UseSoftwareIsoTpForCan)
                {
                    if (!await SetWriteAutoFormat(false))
                    {
                        return false;
                    }
                    if (!await SetCanFilter(this.RxCanId, 0x7FF, CanFilterType.Pass))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!await SetCanFilter(this.RxCanId, 0x7FF, CanFilterType.Flow))
                    {
                        return false;
                    }
                }

                if (await EnableProtocolNetwork() == false)
                {
                    return false;
                }

                this.pendingRawCanFrames.Clear();
                this.softwareIsoTp = UseSoftwareIsoTpForCan;
                this.CanIdPrefixLength = 4;
                this.Supports4X = false;
                this.CurrentProtocol = BusProtocol.Can500k;
                this.Logger.AddDebugMessage($"OBDX CAN ready: 500k, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}, {(UseSoftwareIsoTpForCan ? "software ISO-TP (PASS filter)" : "native ISO-15765 (FLOW filter)")}.");
                return true;
            }

            if (protocol == BusProtocol.Vpw)
            {
                if (!await SetProtocol(OBDProtocols.VPW))
                {
                    return false;
                }
                Response<bool> setup = await DVISetup();
                if (setup.Status != ResponseStatus.Success)
                {
                    return false;
                }

                this.softwareIsoTp = false;
                this.CanIdPrefixLength = 0;
                this.Supports4X = true;
                this.CurrentProtocol = BusProtocol.Vpw;
                this.Logger.AddDebugMessage("OBDX VPW mode restored.");
                return true;
            }

            return false;
        }

        /// <summary>DVI CAN filter types (manual 3.14.5): PASS passes raw matching frames, FLOW also
        /// does on-device ISO-TP reassembly/flow control, BLOCK discards matching frames.</summary>
        private enum CanFilterType : byte { Pass = 0x00, Flow = 0x01, Block = 0x02 }

        /// <summary>
        /// Install a CAN filter (DVI 0x34 "entire filter", sub-command 0x00). Layout:
        /// 34 11 00 [MM NN XX ZZ] [filterId 4] [mask 4] [flowId 4] YY, where MM=filter#,
        /// NN=00(11-bit), XX=type, ZZ=01(on), filterId = ECU response ID, flowId = the TX ID (only
        /// used for a FLOW filter). A PASS filter still filters in hardware to filterId/mask, so the
        /// PC only sees the target's frames - it just doesn't reassemble them.
        /// </summary>
        private async Task<bool> SetCanFilter(uint rxId, uint mask, CanFilterType type)
        {
            byte[] cmd = new byte[2 + 17 + 1];
            cmd[0] = 0x34;
            cmd[1] = 0x11;          // 17 data bytes follow
            cmd[2] = 0x00;          // sub-command: entire filter
            cmd[3] = 0x00;          // filter number 0
            cmd[4] = 0x00;          // frame type: 11-bit
            cmd[5] = (byte)type;    // filter type: PASS / FLOW / BLOCK
            cmd[6] = 0x01;          // status: ON
            cmd[7]  = (byte)(rxId >> 24); cmd[8]  = (byte)(rxId >> 16); cmd[9]  = (byte)(rxId >> 8); cmd[10] = (byte)rxId;   // filter ID = RX
            cmd[11] = (byte)(mask >> 24); cmd[12] = (byte)(mask >> 16); cmd[13] = (byte)(mask >> 8); cmd[14] = (byte)mask;   // mask
            cmd[15] = (byte)(this.TxCanId >> 24); cmd[16] = (byte)(this.TxCanId >> 16); cmd[17] = (byte)(this.TxCanId >> 8); cmd[18] = (byte)this.TxCanId; // flow ID = TX
            cmd[19] = CalcChecksum(cmd);

            await this.Port.Send(cmd);

            Response<Message> response = await ReadDVIPacket(1000);
            if (response.Status == ResponseStatus.Success)
            {
                byte[] val = response.Value.GetBytes();
                if (val.Length > 0 && val[0] == 0x44)
                {
                    this.Logger.AddDebugMessage($"CAN {type} filter configured (id {rxId:X3}, mask 0x{mask:X3}).");
                    return true;
                }
            }

            // The device may have applied the filter even if the ack was unclear; don't hard-fail.
            this.Logger.AddDebugMessage("CAN filter ack unclear; continuing.");
            return true;
        }

        /// <summary>
        /// Turn the device's automatic write framing on/off (DVI 0x34 sub 0x0F, manual 3.14.16). With
        /// it off the device sends our bytes verbatim, so software ISO-TP supplies the PCI/length.
        /// </summary>
        private async Task<bool> SetWriteAutoFormat(bool on)
        {
            byte[] cmd = { 0x34, 0x02, 0x0F, (byte)(on ? 0x01 : 0x00), 0x00 };
            cmd[cmd.Length - 1] = CalcChecksum(cmd);
            await this.Port.Send(cmd);

            Response<Message> response = await ReadDVIPacket(1000);
            if (response.Status == ResponseStatus.Success)
            {
                byte[] val = response.Value.GetBytes();
                if (val.Length > 0 && val[0] == 0x44)
                {
                    this.Logger.AddDebugMessage("CAN write auto-format " + (on ? "on." : "off."));
                    return true;
                }
            }

            this.Logger.AddDebugMessage("CAN write auto-format ack unclear; continuing.");
            return true;
        }

        /// <summary>
        /// Query the tool's supported OBD protocols (DVI 0x22 sub 0x05) and return whether HS CAN
        /// (bit 2 of the first protocol byte) is available.
        /// </summary>
        private async Task<bool> QueryCanSupported()
        {
            byte[] Msg = OBDXProDevice.DVI_Supported_OBD_Protocols.GetBytes();
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            Response<Message> m = await ReadDVIPacket(500);
            if (m.Status != ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("Could not read supported OBD protocols; assuming VPW only.");
                return false;
            }

            // Response: 32 03 05 XX NN YY -> XX is the protocol bitmask, bit 2 = HS CAN.
            byte[] val = m.Value.GetBytes();
            if (val.Length < 5 || val[2] != 0x05)
            {
                return false;
            }
            return (val[3] & 0x04) != 0;
        }

        private Task<bool> SetToFilter(byte Val)
        {
            return this.SetToFilter(Val, true);
        }

        private async Task<bool> SetToFilter(byte Val, bool on)
        {
            byte[] Msg = OBDXProDevice.DVI_Set_To_Filter.GetBytes();
            Msg[3] = Val; // DeviceId.Tool;
            Msg[4] = (byte)(on ? 1 : 0);
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            byte[] RespBytes = new byte[Msg.Length];
            Array.Copy(Msg, RespBytes, Msg.Length);
            RespBytes[0] += (byte)0x10;
            RespBytes[RespBytes.Length - 1] = CalcChecksum(RespBytes);
            Response<Message> response = await ReadDVIPacket();
            if (response.Status == ResponseStatus.Success & Utility.CompareArraysPart(response.Value.GetBytes(), RespBytes))
            {
                this.Logger.AddDebugMessage("Filter set and enabled");
                return true;
            }
            else
            {

                this.Logger.AddDebugMessage("Failed to set filter");
                return false;
            }
        }

        private async Task<bool> EnableProtocolNetwork()
        {
            byte[] Msg = OBDXProDevice.DVI_Set_NewtorkEnable.GetBytes();
            Msg[3] = 1; //on
            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            byte[] RespBytes = new byte[Msg.Length];
            Array.Copy(Msg, RespBytes, Msg.Length);
            RespBytes[0] += (byte)0x10;
            RespBytes[RespBytes.Length - 1] = CalcChecksum(RespBytes);
            Response<Message> response = await ReadDVIPacket();
            if (response.Status == ResponseStatus.Success & Utility.CompareArraysPart(response.Value.GetBytes(), RespBytes))
            {
                this.Logger.AddDebugMessage("Network enabled");
                return true;
            }
            else
            {
                this.Logger.AddDebugMessage("Failed to enable network");
                return false;
            }
        }

        /// <summary>
        /// Set the interface to 1x or 4x speed
        /// </summary>
        /// <remarks>
        /// The caller must also tell the PCM to switch speeds
        /// </remarks>
        protected override async Task<bool> SetVpwSpeedInternal(VpwSpeed newSpeed)
        {

            byte[] Msg = OBDXProDevice.DVI_Set_Speed.GetBytes();

            if (newSpeed == VpwSpeed.Standard)
            {
                this.Logger.AddDebugMessage("DVI setting VPW 1X");
                Msg[3] = 0;
                this.vpwSpeed = VpwSpeed.Standard;

            }
            else
            {
                this.Logger.AddDebugMessage("DVI setting VPW 4X");
                Msg[3] = 1;
                this.vpwSpeed = VpwSpeed.FourX;
            }

            Msg[Msg.Length - 1] = CalcChecksum(Msg);
            await this.Port.Send(Msg);

            byte[] RespBytes = new byte[Msg.Length];
            Array.Copy(Msg, RespBytes, Msg.Length);
            RespBytes[0] += (byte)0x10;
            RespBytes[RespBytes.Length - 1] = CalcChecksum(RespBytes);
            Response<Message> m = await FindResponseFromTool(RespBytes);
            if (m.Status != ResponseStatus.Success) return false;

            return true;
        }


        enum ConfigurationErrors : byte
        {
            InvalidCommand = 1,
            RecvTooLong = 2,
            ByteWaitTimeout = 3,
            InvalidSerialChksum = 4,
            SubCommandIncorrectSize = 5,
            InvalidSubCommand = 6,
            SubCommandInvalidData = 7
        }
        enum NetworkErrors : byte
        {
            ReadBusInactive = 0,
            ReadSOFLongerThenMax = 1,
            ReadSOFShorterThenMin = 2,
            Read2BytesOrLess = 3,
            ReadCRCIncorrect = 4,
            ReadFilterTOdoesNotMatch = 5,
            ReadFilterFROMdoesNotMatch = 6,
            ReadRangeFilterTOdoesNotMatch = 7,
            ReadRangeFilterFROMdoesNotMatch = 8,
            WriteFrameIdleFindTimeout = 9,
            NotEnabled = 10,
            ReadOnlyMode = 11,
            ReadFrameAwaitingSendPC = 12
        }
        enum ErrorType : byte
        {
            ConfigOrTxNetwork = 0,
            RxNetwork = 1
        }


        private void ProcessError(ErrorType Type, byte code)
        {
            string ErrVal = "";
            if (Type == ErrorType.ConfigOrTxNetwork)
            {
                switch (code)
                {
                    case (byte)ConfigurationErrors.InvalidCommand:
                        ErrVal = "Invalid command byte received";
                        break;
                    case (byte)ConfigurationErrors.RecvTooLong:
                        ErrVal = "Sent frame is larger then max allowed frame (4200)";
                        break;
                    case (byte)ConfigurationErrors.ByteWaitTimeout:
                        ErrVal = "Timeout occured waiting for byte to be received from PC";
                        break;
                    case (byte)ConfigurationErrors.InvalidSerialChksum:
                        ErrVal = "Invalid frame checksum sent to scantool";
                        break;
                    case (byte)ConfigurationErrors.SubCommandIncorrectSize:
                        ErrVal = "Sent command had incorrect length";
                        break;
                    case (byte)ConfigurationErrors.InvalidSubCommand:
                        ErrVal = "Invalid sub command detected";
                        break;
                    case (byte)ConfigurationErrors.SubCommandInvalidData:
                        ErrVal = "Invalid data detected for sub command";
                        break;
                }
            }
            else if (Type == ErrorType.RxNetwork)
            {

            }
            this.Logger.AddDebugMessage("Fault reported from scantool: " + ErrVal);
        }

        public override void ClearMessageBuffer()
        {
            try
            {
                this.Port.DiscardBuffers();
            } catch {
                this.Dispose();
            }
        }

        /// <summary>
        /// This is based on the timeouts used by the AllPro, so it could probably be optimized further.
        /// </summary>
        private int GetReceiveTimeout()
        {
            int result;
            if (this.vpwSpeed == VpwSpeed.Standard)
            {
                switch (this.currentTimeoutScenario)
                {
                    case TimeoutScenario.Minimum:
                        result = 50;
                        break;

                    case TimeoutScenario.ReadProperty:
                        result = 50;
                        break;

                    case TimeoutScenario.ReadCrc:
                        result = 3000;
                        break;

                    case TimeoutScenario.ReadMemoryBlock:
                        result = 250;
                        break;

                    case TimeoutScenario.EraseMemoryBlock:
                        result = 7000;
                        break;

                    case TimeoutScenario.WriteMemoryBlock:
                        result = 1200;
                        break;

                    case TimeoutScenario.SendKernel:
                        result = 4000;
                        break;

                    case TimeoutScenario.DataLogging1:
                        result = 25;
                        break;

                    case TimeoutScenario.DataLogging2:
                        result = 40;
                        break;

                    case TimeoutScenario.DataLogging3:
                        result = 60;
                        break;

                    case TimeoutScenario.DataLogging4:
                        result = 80;
                        break;

                    case TimeoutScenario.DataLoggingStreaming:
                        result = 0;
                        break;

                    case TimeoutScenario.Detect:
                        result = 500;
                        break;

                    case TimeoutScenario.Maximum:
                        result = 1020;
                        break;

                    default:
                        throw new NotImplementedException("Unknown timeout scenario " + this.currentTimeoutScenario);
                }
            }
            else
            {
                switch (this.currentTimeoutScenario)
                {
                    case TimeoutScenario.Minimum:
                        result = 50;
                        break;

                    case TimeoutScenario.ReadProperty:
                        result = 50;
                        break;

                    case TimeoutScenario.ReadCrc:
                        result = 3000;
                        break;

                    case TimeoutScenario.ReadMemoryBlock:
                        result = 250;
                        break;

                    case TimeoutScenario.EraseMemoryBlock:
                        result = 7000;
                        break;

                    case TimeoutScenario.WriteMemoryBlock:
                        result = 600;
                        break;

                    case TimeoutScenario.SendKernel:
                        result = 2000;
                        break;

                    case TimeoutScenario.DataLogging1:
                        result = 7;
                        break;

                    case TimeoutScenario.DataLogging2:
                        result = 10;
                        break;

                    case TimeoutScenario.DataLogging3:
                        result = 15;
                        break;

                    case TimeoutScenario.Detect:
                        result = 500;
                        break;

                    case TimeoutScenario.Maximum:
                        result = 1020;
                        break;

                    default:
                        throw new NotImplementedException("Unknown timeout scenario " + this.currentTimeoutScenario);
                }
            }

            return result;
        }

        public async override Task<bool> CheckDeviceConnection() // This should only be called on a device already in DVI mode.
        {
            byte[] sendBytes = OBDXProDevice.DVI_BOARD_NAME.GetBytes();
            sendBytes[sendBytes.Length - 1] = CalcChecksum(sendBytes);
            await this.Port.Send(sendBytes);
            Response<Message> response = await ReadDVIPacket(200);
            if(response.Status == ResponseStatus.Success)
            {
                byte[] val = response.Value.GetBytes();
                string nameTest = System.Text.Encoding.ASCII.GetString(val, 3, val[1] - 1);
                if(nameTest == ToolConnected)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

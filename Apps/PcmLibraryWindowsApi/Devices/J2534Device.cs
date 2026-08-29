// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using J2534DotNet;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace PcmHacking
{
    /// <summary>
    /// This class encapsulates all code that is unique to the AVT 852 interface.
    /// </summary>
    ///
    class J2534Device : Device, ICanTarget, IRawCanMonitor
    {
        /// <summary>
        /// Configuration settings
        /// </summary>
        public int ReadTimeout = 3000;
        public int WriteTimeout = 2000;

        // A J2534 ISO15765 (CAN) WriteMsgs blocks until the whole segmented multi-frame transfer is
        // confirmed by the receiver's flow control. A multi-KB kernel/flash block on a slower device
        // (e.g. Mongoose) can take longer than the 2 s VPW WriteTimeout, which made WriteMsgs return
        // ERR_TIMEOUT even though the transfer completed and the PCM ack'd - the late ack then leaked
        // into the next exchange and looked like an unexpected response. WriteMsgs returns the instant
        // the transfer finishes, so this larger ceiling costs nothing on success; it only prevents a
        // premature false "send failed" while a large CAN block is still in flight.
        public int CanWriteTimeout = 8000;

        /// <summary>
        /// variety of properties used to id channels, fitlers and status
        /// </summary>
        private J2534_Struct J2534Port;
        public List<ulong> Filters = [];
        private int DeviceID;
        private int ChannelID;
        private ProtocolID Protocol;
        public bool IsProtocolOpen;
        public bool IsJ2534Open;
        private const string PortName = "J2534";
        private const uint MessageFilter = 0x6CF010;
        public string ToolName = "";

        /// <summary>Current bus protocol; drives send/receive formatting for this device.</summary>
        private BusProtocol CurrentProtocol = BusProtocol.Vpw;

        /// <summary>Id of the extra pass-all filter installed while monitoring; -1 when not monitoring.</summary>
        private int monitorFilterId = -1;

        /// <summary>True while the channel is in raw CAN mode for monitoring (not ISO15765).</summary>
        private bool monitoringRawCan;

        // CAN target addresses; default from the shared CanId constants, settable so the command
        // layer can address a different module or id.
        /// <summary>CAN ID to transmit to (tool to target).</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN ID to accept (target to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        /// <summary>
        /// global error variable for reading/writing. (Could be done on the fly)
        /// TODO, keep record of all errors for debug
        /// </summary>
        public J2534Err OBDError;

        /// <summary>
        /// J2534 has two parts.
        /// J2534device which has the supported protocols ect as indicated by dll and registry.
        /// J2534extended which is al the actual commands and functions to be used. 
        /// </summary>
        struct J2534_Struct
        {
            public J2534 Functions;
            public J2534DotNet.J2534Device LoadedDevice;
        }

        public J2534Device(J2534DotNet.J2534Device jport, ILogger logger) : base(logger)
        {
            J2534Port = new J2534_Struct();
            J2534Port.Functions = new J2534();
            J2534Port.LoadedDevice = jport;

            // Reduced from 4096+12 for the MDI2
            this.MaxSendSize = 2048 + 12;    // J2534 Standard is 4KB
            this.MaxReceiveSize = 2048 + 12; // J2534 Standard is 4KB
            this.Supports4X = true;
            this.SupportsSingleDpidLogging = true;
            this.SupportsStreamLogging = true;
        }

        protected override void Dispose(bool disposing)
        {
            DisconnectTool();
        }

        public override string ToString()
        {
            string? deviceName = this.J2534Port.LoadedDevice?.Name;
            return string.IsNullOrEmpty(deviceName)
                ? "J2534 Device"
                : "J2534 " + deviceName;
        }

        public override string GetDeviceType()
        {
            return PortName;
        }

        // This needs to return Task<bool> for consistency with the Device base class.
        // However it doesn't do anything asynchronous, so to make the code more readable
        // it just wraps a private method that does the real work and returns a bool.
        public override async Task<bool> Initialize()
        {
            try
            {
                return await Task.FromResult(this.InitializeInternal());
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        // This returns 'bool' for the sake of readability. That bool needs to be
        // wrapped in a Task object for the public Initialize method.
        private bool InitializeInternal()
        {
            Filters = new List<ulong>();

            this.Logger.AddUserMessage("Initializing " + this.ToString());

            Response<J2534Err> m; // hold returned messages for processing
            Response<bool> m2;
            Response<double> volts;

            // Check J2534 API
            //this.Logger.AddDebugMessage(J2534Port.Functions.ToString());

            // Check not already loaded
            if (IsLoaded == true)
            {
                // Only disconnect protocol if it was actually opened - a failed previous
                // init may have left IsLoaded true but never reached ConnectToProtocol.
                if (IsProtocolOpen)
                {
                    try
                    {
                        m = DisconnectFromProtocol();
                    }
                    catch
                    {
                        CloseLibrary();
                        IsJ2534Open = false;
                        return false;
                    }
                    if (m.Status != ResponseStatus.Success)
                    {
                        this.Logger.AddUserMessage("Error disconnecting from protocol.");
                        return false;
                    }
                    this.Logger.AddDebugMessage("Successfully disconnected from protocol.");
                }

                // Only disconnect tool if it was actually opened.
                if (IsJ2534Open)
                {
                    m = DisconnectTool();
                    if (m.Status != ResponseStatus.Success)
                    {
                        this.Logger.AddUserMessage("Error disconnecting from tool.");
                        return false;
                    }
                    this.Logger.AddDebugMessage("Successfully disconnected from tool.");
                }
                else
                {
                    // DLL is loaded but tool was never opened - just unload the DLL.
                    CloseLibrary();
                }
            }

            // Connect to requested DLL
            m2 = LoadLibrary(J2534Port.LoadedDevice);
            if (m2.Status != ResponseStatus.Success)
            {
                // The path is worth showing: a driver registered only for the other bitness is
                // listed but cannot be loaded, and the Program Files tree it sits in says which.
                this.Logger.AddUserMessage("Unable to load the J2534 DLL: " + J2534Port.LoadedDevice.FunctionLibrary);
                return false;
            }
            this.Logger.AddUserMessage("Loaded DLL");

            // Connect to scantool
            m = ConnectTool();
            if (m.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("Unable to connect to the device.");
                return false;
            }

            this.Logger.AddUserMessage("Connected to the device.");

            // Optional.. read API,firmware version ect here

            // Read voltage
            volts = ReadVoltage();
            if (volts.Status != ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("Unable to read battery voltage.");
            }
            else
            {
                this.Logger.AddUserMessage("Battery Voltage is: " + volts.Value.ToString());
            }

            // Start on VPW, which is what most of these PCMs use. A CAN-only interface has no VPW
            // channel to open, so start it on CAN instead of failing initialization outright;
            // SetProtocol moves either kind of device to the bus an operation needs.
            if (!this.J2534Port.LoadedDevice.IsJ1850VPWSupported)
            {
                this.Logger.AddUserMessage("This device does not support VPW. Initializing on CAN.");
                return SetProtocolInternal(BusProtocol.Can500k);
            }

            // Set Protocol
            m = ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_10400, ConnectFlag.NONE);
            if (m.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("Failed to set protocol, J2534 error code: 0x" + m.Value.ToString("X"));
                return false;
            }
            this.Logger.AddDebugMessage("Protocol Set");

            // Set filter
            m = SetFilter(0xFEFFFF, J2534Device.MessageFilter, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            if (m.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                return false;
            }

            this.Logger.AddDebugMessage("Device initialization complete.");

            return true;
        }

        /// <summary>
        /// Set the receive timeout for the given scenario. Previously a no-op, which left every
        /// J2534 ReadMsgs blocking for the full 3000 ms even when probing - far too slow for a
        /// multi-bus scan. Now the scan-probe budget (Detect) is honored; all other scenarios keep
        /// the existing 3000 ms so normal operations are unchanged.
        /// </summary>
        public override Task<TimeoutScenario> SetTimeout(TimeoutScenario scenario)
        {
            TimeoutScenario previous = this.currentTimeoutScenario;
            this.currentTimeoutScenario = scenario;
            switch (scenario)
            {
                case TimeoutScenario.Detect:
                    // Fast empty-bus ruling during a multi-bus scan.
                    this.ReadTimeout = 500;
                    break;

                case TimeoutScenario.EraseMemoryBlock:
                    // A flash sector erase runs for seconds and the kernel only emits its
                    // responsePending keepalive every few seconds. The read window must be longer than
                    // that interval so the pending lands inside one Receive() and the wait is extended
                    // (see Device.ReceiveMessage) instead of timing out between keepalives.
                    this.ReadTimeout = 8000;
                    break;

                default:
                    // All other scenarios keep the original 3000 ms so normal operations are unchanged.
                    this.ReadTimeout = 3000;
                    break;
            }
            return Task.FromResult(previous);
        }

        /// <summary>
        /// This will process incoming messages for up to 500ms looking for a message
        /// </summary>
        public async Task<Response<Message>> FindResponse(Message expected)
        {
            //this.Logger.AddDebugMessage("FindResponse called");
            for (int iterations = 0; iterations < 5; iterations++)
            {
                Message response = await this.ReceiveMessage();
                if (Utility.CompareArraysPart(response.GetBytes(), expected.GetBytes()))
                {
                    return Response.Create(ResponseStatus.Success, response);
                }
                await Task.Delay(100);
            }

            return Response.Create(ResponseStatus.Timeout, (Message)null!);
        }

        /// <summary>
        /// Read an network packet from the interface, and return a Response/Message
        /// </summary>
        protected override Task Receive()
        {
            //this.Logger.AddDebugMessage("Trace: Read Network Packet");

            int NumMessages = 1;
            List<PassThruMsg> rxMsgs = new List<PassThruMsg>();
            PassThruMsg PassMess;
            OBDError = 0; // Clear any previous faults

            // Read until we deliver a frame the active inbound filter accepts (the response this
            // exchange is waiting for) or the device read times out. Frames the filter rejects - the
            // interface's own transmit echo (e.g. 00 00 07 E0) or a stray ack left from a previous
            // exchange - are skipped here and kept out of the queue, so a burst of them can never be
            // mistaken for silence and time the exchange out. Bounded by ReadTimeout so a continuously
            // busy bus cannot wedge the read.
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (sw.ElapsedMilliseconds <= (long)ReadTimeout)
            {
                NumMessages = 1;
                OBDError = J2534Port.Functions.ReadMsgs((int)ChannelID, ref rxMsgs, ref NumMessages, ReadTimeout);
                if (OBDError != J2534Err.STATUS_NOERROR)
                {
                    // No (more) frames within the read window: a genuine transport timeout.
                    this.Logger.AddDebugMessage("ReadMsgs OBDError: " + OBDError);
                    return Task.FromResult(0);
                }

                PassMess = rxMsgs.Last();
                if ((int)PassMess.RxStatus == (((int)RxStatus.NONE) + ((int)RxStatus.TX_MSG_TYPE)) || (PassMess.RxStatus == RxStatus.START_OF_MESSAGE))
                {
                    // Transmit echo / start-of-message marker, not a response: read again.
                    continue;
                }

                byte[] rxData = PassMess.Data;
                if (this.CurrentProtocol == BusProtocol.Can500k)
                {
                    if (rxData.Length <= 4)
                    {
                        // Header-only CAN frame: the 4-byte transmit echo / TxDone with no UDS payload
                        // (some J2534 stacks surface it without the TX_MSG_TYPE flag). Not a response -
                        // keep reading so we read past it to the real reply rather than handing the
                        // upper layers a bare CAN id (which looked like an "unexpected response").
                        continue;
                    }

                    // ISO15765 frames are prefixed with the 4-byte CAN ID; strip it so the upper layers
                    // see the bare UDS payload (J2534 already reassembled any multi-frame message).
                    byte[] stripped = new byte[rxData.Length - 4];
                    Array.Copy(rxData, 4, stripped, 0, stripped.Length);
                    rxData = stripped;
                }

                if (this.Enqueue(new Message(rxData, (ulong)PassMess.Timestamp, (ulong)OBDError), logReceived: false))
                {
                    // On-conversation response queued for this exchange: report the whole payload once.
                    if (this.CurrentProtocol == BusProtocol.Can500k)
                    {
                        this.Logger.AddDebugMessage($"RX: {this.RxCanId:X3} {rxData.ToHex()}");
                    }
                    else
                    {
                        this.Logger.AddDebugMessage("RX: " + rxData.ToHex());
                    }
                    return Task.FromResult(0);
                }
                // Off-conversation frame, dropped by the inbound filter: keep reading for the response.
            }

            return Task.FromResult(0);
        }

        /// <summary>
        /// Convert a Message to an J2534 formatted transmit, and send to the interface
        /// </summary>
        private Response<J2534Err> SendNetworkMessage(Message message, TxFlag Flags, int writeTimeout)
        {
            //this.Logger.AddDebugMessage("Trace: Send Network Packet");

            PassThruMsg TempMsg = new PassThruMsg(Protocol, Flags, message.GetBytes());

            int NumMsgs = 1;

            OBDError = J2534Port.Functions.WriteMsgs((int)ChannelID, ref TempMsg, ref NumMsgs, writeTimeout);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                // Debug messages here...check why failed..
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Send a message, wait for a response, return the response.
        /// </summary>
        public override Task<bool> SendMessage(Message message)
        {
            //this.Logger.AddDebugMessage("Send request called");
            Response<J2534Err> MyError;

            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                // Prepend the 4-byte destination CAN ID; J2534 ISO15765 adds the ISO-TP framing.
                byte[] uds = message.GetBytes();
                byte[] data = new byte[4 + uds.Length];
                byte[] idBytes = CanIdToBytes(this.TxCanId);
                Array.Copy(idBytes, 0, data, 0, 4);
                Array.Copy(uds, 0, data, 4, uds.Length);
                this.Logger.AddDebugMessage($"TX: {this.TxCanId:X3} {uds.ToHex()}");
                // ISO15765 send blocks until the segmented transfer completes; give large CAN blocks
                // enough headroom (see CanWriteTimeout) so a slow device is not falsely failed.
                MyError = SendNetworkMessage(new Message(data), TxFlag.ISO15765_FRAME_PAD, this.CanWriteTimeout);
            }
            else
            {
                this.Logger.AddDebugMessage("TX: " + message.GetBytes().ToHex());
                MyError = SendNetworkMessage(message, TxFlag.NONE, this.WriteTimeout);
            }

            if (MyError.Status != ResponseStatus.Success)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Load in dll
        /// </summary>
        private Response<bool> LoadLibrary(J2534DotNet.J2534Device TempDevice)
        {
            ToolName = TempDevice.Name;
            J2534Port.LoadedDevice = TempDevice;
            if (J2534Port.Functions.LoadLibrary(J2534Port.LoadedDevice))
            {
                return Response.Create(ResponseStatus.Success, true);
            }
            else
            {
                return Response.Create(ResponseStatus.Error, false);
            }
        }

        /// <summary>
        /// Unload dll
        /// </summary>
        private Response<bool> CloseLibrary()
        {
            if (J2534Port.Functions.FreeLibrary())
            {
                return Response.Create(ResponseStatus.Success, true);
            }
            else
            {
                return Response.Create(ResponseStatus.Error, false);
            }
        }

        /// <summary>
        /// Connects to physical scantool
        /// </summary>
        private Response<J2534Err> ConnectTool()
        {
            DeviceID = 0;
            ChannelID = 0;
            Filters.Clear();
            OBDError = 0;
            OBDError = J2534Port.Functions.Open(ref DeviceID);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                IsJ2534Open = false;
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            else
            {
                IsJ2534Open = true;
                return Response.Create(ResponseStatus.Success, OBDError);
            }
        }

        /// <summary>
        /// Disconnects from physical scantool
        /// </summary>
        private Response<J2534Err> DisconnectTool()
        {
            try
            {
                OBDError = J2534Port.Functions.Close((int)DeviceID);
            }
            catch
            {
                IsJ2534Open = false;
                CloseLibrary();
                return Response.Create(ResponseStatus.Success, OBDError);
            }
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                // Big problems, do something here
            }
            IsJ2534Open = false;
            CloseLibrary();
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Keep record if DLL has been loaded
        /// </summary>
        public bool IsLoaded
        {
            get
            {
                try
                {

                    Process proc = Process.GetCurrentProcess();
                    foreach (ProcessModule dll in proc.Modules)
                    {
                        if (dll.FileName == J2534Port.LoadedDevice.FunctionLibrary)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Logger.AddDebugMessage(ex.Message);

                }
                return false;
            }
        }

        /// <summary>
        /// Connect to selected protocol
        /// Must provide protocol, speed, connection flags, recommended optional is pins
        /// </summary>
        private Response<J2534Err> ConnectToProtocol(ProtocolID ReqProtocol, BaudRate Speed, ConnectFlag ConnectFlags)
        {
            OBDError = J2534Port.Functions.Connect(DeviceID, ReqProtocol, ConnectFlags, Speed, ref ChannelID);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            Protocol = ReqProtocol;
            IsProtocolOpen = true;
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Disconnect from protocol
        /// </summary>
        private Response<J2534Err> DisconnectFromProtocol()
        {
            if (!IsProtocolOpen)
            {
                return Response.Create(ResponseStatus.Success, J2534Err.STATUS_NOERROR);
            }
            OBDError = J2534Port.Functions.Disconnect((int)ChannelID);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            IsProtocolOpen = false;
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Select the bus protocol the device communicates on. Reconnects the J2534 channel to
        /// J1850VPW or ISO15765 (500k CAN) and installs the matching filter. ISO15765 makes the
        /// device handle ISO-TP segmentation, reassembly and flow control internally (native
        /// ISO-TP, like the OBDX), so no software transport is used. No async work, so this wraps
        /// a synchronous helper for readability.
        /// </summary>
        public override Task<bool> SetProtocol(BusProtocol protocol)
        {
            try
            {
                return Task.FromResult(SetProtocolInternal(protocol));
            }
            catch (Exception ex)
            {
                this.Logger.AddDebugMessage("J2534 SetProtocol error: " + ex.Message);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Whether the installed driver declares a channel for this bus. CAN is read from the
        /// ISO15765 channel count, because that is the mode these PCMs are talked to in.
        /// </summary>
        private bool SupportsProtocol(BusProtocol protocol)
        {
            switch (protocol)
            {
                case BusProtocol.Vpw:
                    return this.J2534Port.LoadedDevice.IsJ1850VPWSupported;

                case BusProtocol.Can500k:
                    return this.J2534Port.LoadedDevice.IsISO15765Supported;

                default:
                    return false;
            }
        }

        private bool SetProtocolInternal(BusProtocol protocol)
        {
            // The open channel has to match, not just the recorded protocol: a failed switch (asking
            // a VPW-only interface for CAN, say) disconnects the old channel before finding out it
            // cannot open the new one, which would otherwise leave this claiming to be on a bus whose
            // channel is closed.
            if (protocol == this.CurrentProtocol && this.IsProtocolOpen)
            {
                return true;
            }

            // The driver declares its channels in the registry, so a bus it does not have is refused
            // here rather than by disconnecting the working channel and then failing to open the new
            // one.
            if (!this.SupportsProtocol(protocol))
            {
                return false;
            }

            if (protocol == BusProtocol.Can500k)
            {
                DisconnectFromProtocol();
                Filters.Clear();

                Response<J2534Err> c = ConnectToProtocol(ProtocolID.ISO15765, BaudRate.ISO15765, ConnectFlag.NONE);
                if (c.Status != ResponseStatus.Success)
                {
                    this.Logger.AddUserMessage("J2534: failed to open ISO15765 (CAN) channel, error 0x" + c.Value.ToString("X"));
                    return false;
                }

                // A flow-control filter lets J2534 auto-handle multi-frame ISO-TP and send FC frames.
                Response<J2534Err> f = SetCanFlowControlFilter();
                if (f.Status != ResponseStatus.Success)
                {
                    this.Logger.AddDebugMessage("J2534: CAN flow-control filter warning 0x" + f.Value.ToString("X") + " (may still work).");
                }

                this.Supports4X = false;
                this.CurrentProtocol = BusProtocol.Can500k;
                this.Logger.AddDebugMessage($"J2534 CAN ready: 500k ISO15765, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}.");
                return true;
            }

            if (protocol == BusProtocol.Vpw)
            {
                DisconnectFromProtocol();
                Filters.Clear();

                Response<J2534Err> c = ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_10400, ConnectFlag.NONE);
                if (c.Status != ResponseStatus.Success)
                {
                    this.Logger.AddUserMessage("J2534: failed to re-open J1850VPW channel, error 0x" + c.Value.ToString("X"));
                    return false;
                }

                SetFilter(0xFEFFFF, J2534Device.MessageFilter, 0, TxFlag.NONE, FilterType.PASS_FILTER);

                this.Supports4X = true;
                this.CurrentProtocol = BusProtocol.Vpw;
                this.Logger.AddDebugMessage("J2534 VPW mode restored.");
                return true;
            }

            return false;
        }

        /// <summary>VPW and CAN 500k can both be monitored on this device.</summary>
        public override IReadOnlyList<BusProtocol> MonitorableProtocols { get; } = new[] { BusProtocol.Vpw, BusProtocol.Can500k };

        /// <summary>
        /// Begin monitoring. For VPW the init filter only passes the tool/PCM conversation, so install
        /// a pass-all (mask 0) instead. For CAN the operational mode is ISO15765 (reassembled, single
        /// id); switch to raw CAN at 500k with a pass-all filter so every id - including 101 and the
        /// request side - is seen as raw frames. Filters are cleared first because some channels have
        /// only one filter slot.
        /// </summary>
        public override async Task<bool> BeginMonitor(BusProtocol protocol)
        {
            if (protocol == BusProtocol.Can500k)
            {
                return this.BeginRawCanMonitor();
            }

            if (!await this.SetProtocol(protocol))
            {
                return false;
            }

            if (protocol == BusProtocol.Vpw)
            {
                this.StopAllFilters();
                Response<J2534Err> f = SetFilter(0x000000, 0x000000, 0, TxFlag.NONE, FilterType.PASS_FILTER);
                if (f.Status == ResponseStatus.Success)
                {
                    this.monitorFilterId = (int)Filters[Filters.Count - 1];
                }
                else
                {
                    this.Logger.AddDebugMessage("Bus monitor: pass-all filter failed, error 0x" + f.Value.ToString("X"));
                }
            }

            return true;
        }

        public override Task EndMonitor()
        {
            if (this.monitoringRawCan)
            {
                // Restore the operational ISO15765 (native ISO-TP) CAN channel for later read/write.
                this.monitoringRawCan = false;
                DisconnectFromProtocol();
                Filters.Clear();
                ConnectToProtocol(ProtocolID.ISO15765, BaudRate.ISO15765, ConnectFlag.NONE);
                SetCanFlowControlFilter();
            }
            else if (this.monitorFilterId >= 0)
            {
                this.monitorFilterId = -1;
                // Drop the pass-all and restore the normal tool/PCM filter for later operations.
                this.StopAllFilters();
                SetFilter(0xFEFFFF, J2534Device.MessageFilter, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            }

            return Task.CompletedTask;
        }

        /// <summary>Open a raw CAN channel at 500k with a pass-all filter for monitoring.</summary>
        private bool BeginRawCanMonitor()
        {
            DisconnectFromProtocol();
            Filters.Clear();

            Response<J2534Err> c = ConnectToProtocol(ProtocolID.CAN, BaudRate.CAN, ConnectFlag.NONE);
            if (c.Status != ResponseStatus.Success)
            {
                this.Logger.AddUserMessage("J2534: failed to open raw CAN channel, error 0x" + c.Value.ToString("X"));
                return false;
            }

            Response<J2534Err> f = SetCanPassAllFilter();
            if (f.Status != ResponseStatus.Success)
            {
                this.Logger.AddDebugMessage("J2534 raw CAN pass-all filter failed, error 0x" + f.Value.ToString("X"));
            }

            this.Supports4X = false;
            this.monitoringRawCan = true;
            this.CurrentProtocol = BusProtocol.Can500k;
            this.Logger.AddDebugMessage("J2534 raw CAN monitor: 500k, pass-all.");
            return true;
        }

        /// <summary>Install a pass-all (mask 0) PASS filter on the raw CAN channel.</summary>
        private Response<J2534Err> SetCanPassAllFilter()
        {
            byte[] mask    = CanIdToBytes(0x00000000);
            byte[] pattern = CanIdToBytes(0x00000000);

            PassThruMsg maskMsg    = new PassThruMsg(ProtocolID.CAN, TxFlag.NONE, mask);
            PassThruMsg patternMsg = new PassThruMsg(ProtocolID.CAN, TxFlag.NONE, pattern);
            int filterId = 0;

            OBDError = J2534Port.Functions.StartMsgFilter(ChannelID, FilterType.PASS_FILTER,
                ref maskMsg, ref patternMsg, ref filterId);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }

            Filters.Add((ulong)filterId);
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// IRawCanMonitor: read one raw CAN frame (id + payload) in raw CAN monitor mode. Skips TX echo
        /// and keeps short frames (a 101 broadcast carries only a few bytes). (0, empty) on timeout.
        /// </summary>
        public Task<(uint id, byte[] frame)> ReceiveCanFrame()
        {
            List<PassThruMsg> rxMsgs = new List<PassThruMsg>();
            Stopwatch sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds <= (long)ReadTimeout)
            {
                int numMessages = 1;
                OBDError = J2534Port.Functions.ReadMsgs((int)ChannelID, ref rxMsgs, ref numMessages, ReadTimeout);
                if (OBDError != J2534Err.STATUS_NOERROR)
                {
                    return Task.FromResult((0u, Array.Empty<byte>()));
                }

                PassThruMsg msg = rxMsgs.Last();
                if ((int)msg.RxStatus == (((int)RxStatus.NONE) + ((int)RxStatus.TX_MSG_TYPE)) || (msg.RxStatus == RxStatus.START_OF_MESSAGE))
                {
                    continue;
                }

                byte[] data = msg.Data;
                if (data == null || data.Length < 4)
                {
                    continue;
                }

                uint id = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
                byte[] payload = new byte[data.Length - 4];
                Array.Copy(data, 4, payload, 0, payload.Length);
                return Task.FromResult((id, payload));
            }

            return Task.FromResult((0u, Array.Empty<byte>()));
        }

        /// <summary>Stop and forget every installed message filter.</summary>
        private void StopAllFilters()
        {
            foreach (ulong filterId in Filters)
            {
                J2534Port.Functions.StopMsgFilter((int)ChannelID, (int)filterId);
            }

            Filters.Clear();
        }

        /// <summary>
        /// Install an ISO15765 flow-control filter: pass frames with the ECU response id (RxCanId)
        /// and auto-send flow control using the request id (TxCanId).
        /// </summary>
        private Response<J2534Err> SetCanFlowControlFilter()
        {
            byte[] mask    = CanIdToBytes(0xFFFFFFFF);
            byte[] pattern = CanIdToBytes(this.RxCanId);
            byte[] fc      = CanIdToBytes(this.TxCanId);

            PassThruMsg maskMsg    = new PassThruMsg(ProtocolID.ISO15765, TxFlag.NONE, mask);
            PassThruMsg patternMsg = new PassThruMsg(ProtocolID.ISO15765, TxFlag.NONE, pattern);
            PassThruMsg fcMsg      = new PassThruMsg(ProtocolID.ISO15765, TxFlag.NONE, fc);
            int filterId = 0;

            OBDError = J2534Port.Functions.StartMsgFilter(ChannelID, FilterType.FLOW_CONTROL_FILTER,
                ref maskMsg, ref patternMsg, ref fcMsg, ref filterId);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }

            Filters.Add((ulong)filterId);
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        private static byte[] CanIdToBytes(uint id) => new byte[]
        {
            (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id
        };

        /// <summary>
        /// Read battery voltage
        /// </summary>
        public Response<double> ReadVoltage()
        {
            double Volts = 0;
            int VoltsAsInt = 0;
            OBDError = J2534Port.Functions.ReadBatteryVoltage((int)DeviceID, ref VoltsAsInt);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, Volts);
            }
            else
            {
                Volts = VoltsAsInt / 1000.0;
                return Response.Create(ResponseStatus.Success, Volts);
            }
        }

        /// <summary>
        /// Set filter
        /// </summary>
        private Response<J2534Err> SetFilter(UInt32 Mask, UInt32 Pattern, UInt32 FlowControl, TxFlag txflag, FilterType Filtertype)
        {
            PassThruMsg maskMsg = new PassThruMsg(Protocol, txflag, new Byte[] { (byte)(0xFF & (Mask >> 16)), (byte)(0xFF & (Mask >> 8)), (byte)(0xFF & Mask) });
            PassThruMsg patternMsg = new PassThruMsg(Protocol, txflag, new Byte[] { (byte)(0xFF & (Pattern >> 16)), (byte)(0xFF & (Pattern >> 8)), (byte)(0xFF & Pattern) });
            int tempfilter = 0;
            OBDError = J2534Port.Functions.StartMsgFilter(ChannelID, Filtertype, ref maskMsg, ref patternMsg, ref tempfilter);

            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            Filters.Add((ulong)tempfilter);
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Set the interface to low (false) or high (true) speed
        /// </summary>
        /// <remarks>
        /// The caller must also tell the PCM to switch speeds
        /// </remarks>
        protected override Task<bool> SetVpwSpeedInternal(VpwSpeed newSpeed)
        {
            if (newSpeed == VpwSpeed.Standard)
            {
                this.Logger.AddDebugMessage("J2534 setting VPW 1X");
                // Disconnect from current protocol
                DisconnectFromProtocol();

                // Connect at new speed
                ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_10400, ConnectFlag.NONE);

                // Set Filter
                SetFilter(0xFEFFFF, J2534Device.MessageFilter, 0, TxFlag.NONE, FilterType.PASS_FILTER);
                //if (m.Status != ResponseStatus.Success)
                //{
                //    this.Logger.AddDebugMessage("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                //    return false;
                //}


            }
            else
            {
                this.Logger.AddDebugMessage("J2534 setting VPW 4X");
                // Disconnect from current protocol
                DisconnectFromProtocol();

                // Connect at new speed
                ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_41600, ConnectFlag.NONE);

                // Set Filter
                SetFilter(0xFEFFFF, J2534Device.MessageFilter, 0, TxFlag.NONE, FilterType.PASS_FILTER);

            }

            return Task.FromResult(true);
        }

        public override void ClearMessageBuffer()
        {
            J2534Port.Functions.ClearRxBuffer((int)DeviceID);
            J2534Port.Functions.ClearTxBuffer((int)DeviceID);
        }

        public override async Task<byte?> ReadBroadcastState(byte command)
        {
            int readTimeoutBackup = this.ReadTimeout;
            this.ReadTimeout = 200; // Shorten timeout for this check since we expect a response immediately if the command is broadcasting
            try
            {
                Message incoming = await ReceiveMessage();
                return MatchBroadcast(incoming, command);
            }
            catch
            {
                return null;
            }
            finally
            {
                this.ReadTimeout = readTimeoutBackup; // Restore original timeout
            }
        }

        public override Task<bool> CheckDeviceConnection()
        {
            try
            {
                if (Initialize().Result)
                {
                    return Task.FromResult(true);
                }
            }
            catch
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(false);
        }
    }
}

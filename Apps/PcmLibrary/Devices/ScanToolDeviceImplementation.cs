// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// This class encapsulates all code that is unique to the ScanTool MX interface.
    /// </summary>
    public class ScanToolDeviceImplementation : ElmDeviceImplementation
    {
        /// <summary>
        /// Device type for use in the Device Picker dialog box, and for internal comparisons.
        /// </summary>
        public const string DeviceType = "ObdLink ScanTool";
        
        /// <summary>
        /// Constructor.
        /// </summary>
        public ScanToolDeviceImplementation(
            Action<Message> enqueue,
            Func<int> getRecievedMessageCount,
            IPort port, 
            ILogger logger) : 
            base(enqueue, getRecievedMessageCount, port, logger)
        {
            // Both of these numbers could be slightly larger, but round numbers are easier to work with,
            // and these are only used with the Scantool SX interface anyhow. If we detect an AllPro
            // adapter we'll overwrite these values, see the Initialize method below.

            // Please keep the left side easy to read in hex. Then add 12 bytes for VPW overhead.
            // The STPX approach to sending messages should work with larger buffers, but when I tried
            // with my SX, it didn't work. That might only work with the MX (bluetooth version).
            this.MaxSendSize = 192 + 12;

            // The ScanTool SX will download 512kb in roughly 30 minutes at 500 bytes per read.
            // ScanTool reliability suffers at 508 bytes or more, so we're going with a number
            // that's round in base 10 rather than in base 2.
            this.MaxReceiveSize = 500 + 12;

            // This would need a firmware upgrade at the very least, and likely isn't even possible 
            // with current hardware.
            this.Supports4X = false;

            // In theory we could use ATMA or STMA to monitor the bus and read data log streams.
            // In practice I couldn't get that to work. See SetTimeout & SetTimeoutMilliseconds.
            this.SupportsStreamLogging = false;
        }

        /// <summary>
        /// Current bus protocol. The STN performs ISO 15765 (ISO-TP) in firmware, so CAN here is
        /// native ISO-TP: whole GMLAN/UDS payloads go in and out, and the device does the
        /// segmentation and flow-control handshake (the same model as the OBDX native path).
        /// </summary>
        protected BusProtocol CurrentProtocol { get; private set; } = BusProtocol.Vpw;

        // CAN target addresses. Default to the standard OBD2 PCM ids (from the shared CanId
        // constants), but are settable so the command layer can address a different module.
        /// <summary>CAN id used when transmitting (tool to module).</summary>
        public uint TxCanId { get; set; } = CanId.PcmPhysicalRequest;

        /// <summary>CAN id accepted when receiving (module to tool).</summary>
        public uint RxCanId { get; set; } = CanId.PcmPhysicalResponse;

        // The CAN id currently programmed as the AT SH transmit header, or -1 if not yet set.
        // Tracked so we only re-issue AT SH when the target id actually changes.
        private long currentCanHeader = -1;

        // Largest CAN ISO-TP message this device transmits, and the unit the kernel upload is divided
        // into (the command layer splits by MaxKernelSendSize, mirroring the VPW PCMExecute upload).
        // The GM CAN boot loader expects the kernel as a small number of large transfer blocks - a
        // low-address executing block carrying the entry, preceded by a high-address copy block - so
        // this needs to be large enough to keep a ~2 KB kernel to two blocks (one block can't exceed
        // half the kernel plus a header). Measured limits on a small OBDLink: 1024 is accepted, 2048
        // returns OUT OF MEMORY, so 1536 sits in between and still yields two blocks. Adjust to the
        // largest size a given model accepts; if it can't reach ~1100 the kernel won't fit in two
        // blocks.
        public const int CanMaxMessageSize = 512;

        /// <summary>
        /// This string is what will appear in the drop-down list in the UI.
        /// </summary>
        public override string GetDeviceType()
        {
            return DeviceType;
        }

        /// <summary>
        /// Confirm that we're actually connected to the right device, and initialize it.
        /// </summary>
        public override async Task<bool> Initialize()
        {
            this.Logger.AddDebugMessage("Determining whether " + this.ToString() + " is connected.");
            
            try
            {
                string stID = await this.SendRequest("ST I");                 // Identify (ScanTool.net)
                if (stID == "?" || string.IsNullOrEmpty(stID))
                {
                    this.Logger.AddDebugMessage("This is not a ScanTool device.");
                    return false;
                }

                this.Logger.AddUserMessage("ScanTool device ID: " + stID);

                // The following table was provided by ScanTool.net Support - ticket #33419
                // Device                     Max Msg Size    Max Tested Baudrate
                // STN1110                    2k              2 Mbps *
                // STN1130 (OBDLink SX)       2k              2 Mbps *
                // STN1150 (OBDLink MX v1)    2k              N/A
                // STN1151 (OBDLink MX v2)    2k              N/A
                // STN1155 (OBDLink LX)       2k              N/A
                // STN1170                    2k              2 Mbps *
                // STN2100                    4k              2 Mbps
                // STN2120                    4k              2 Mbps
                // STN2230 (OBDLink EX)       4k              N/A
                // STN2255 (OBDLink MX+ v3?)  4k              N/A
                // STB2256 (OBDLink MX+ v4?)  4k              N/A
                //
                // * With character echo off (ATE 0), 1 Mbps with character echo on (ATE 1)

                this.MaxSendSize = 1024 + 12;
                this.MaxReceiveSize = 1024 + 12;

                // Setting timeout to a large value. Since we use STPX commands,
                // the device will stop listening when it receives the expected
                // number of responses, rather than waiting for the timeout.
                this.Logger.AddDebugMessage(await this.SendRequest("STPTO 1000"));

            }
            catch (Exception exception)
            {
                this.Logger.AddDebugMessage("Unable to initalize " + this.ToString());
                this.Logger.AddDebugMessage(exception.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get the time required for the given scenario.
        /// </summary>
        public override int GetTimeoutMilliseconds(TimeoutScenario scenario, VpwSpeed speed)
        {
            int milliseconds;

            if (speed == VpwSpeed.Standard)
            {
                switch (scenario)
                {
                    case TimeoutScenario.Minimum:
                        milliseconds = 0;
                        break;

                    case TimeoutScenario.ReadProperty:
                        milliseconds = 25;
                        break;

                    case TimeoutScenario.Detect:
                        // Short, fixed probe timeout so an empty bus is ruled out quickly during a scan.
                        milliseconds = 500;
                        break;

                    case TimeoutScenario.ReadCrc:
                        milliseconds = 4000;
                        break;

                    case TimeoutScenario.ReadMemoryBlock:
                        milliseconds = 250;
                        break;

                    case TimeoutScenario.EraseMemoryBlock:
                        milliseconds = 7000;
                        break;

                    case TimeoutScenario.WriteMemoryBlock:
                        milliseconds = 250; // Bluetooth needs a touch longer. (MX+ tested)
                        break;

                    case TimeoutScenario.SendKernel:
                        milliseconds = 50;
                        break;

                    case TimeoutScenario.DataLogging1:
                        milliseconds = 25;
                        break;

                    case TimeoutScenario.DataLogging2:
                        milliseconds = 40;
                        break;

                    case TimeoutScenario.DataLogging3:
                        milliseconds = 60;
                        break;

                    case TimeoutScenario.DataLogging4:
                        milliseconds = 80;
                        break;

                    case TimeoutScenario.DataLoggingStreaming:
                        // This is hacky, but the code path is not supported anyway.
                        // I had hoped to use ATMA or STMA to monitor the bus and log
                        // data, but that hasn't worked.  Also see SetTimeoutMilliseconds.
                        milliseconds = -1;
                        break;

                    case TimeoutScenario.Maximum:
                        return 1020;

                    default:
                        throw new NotImplementedException("Unknown timeout scenario " + scenario);
                }
            }
            else
            {
                throw new NotImplementedException("Since when did ScanTool devices support 4x?");
            }

            return milliseconds;
        }

        /// <summary>
        /// Set the timeout to the device. If this is set too low, the device
        /// will return 'No Data'. The ST Equivalent timeout command doesn't have
        /// the same 1020 millisecond limit since it takes an integer milliseconds
        /// as a paramter.
        /// </summary>
        public override async Task<bool> SetTimeoutMilliseconds(int milliseconds)
        {
            if (milliseconds == -1)
            {
                // This doesn't actually work yet, but I think it should be possible.
                // To test this code path, change this value in the constructor:
                // this.SupportsStreamLogging = false;
                return await this.SendAndVerify("STMA", "");
            }
            else
            {
                return await this.SendAndVerify("STPTO " + milliseconds, "OK");
            }           
        }

        /// <summary>
        /// Send a message, do not expect a response.
        /// </summary>
        /// <remarks>
        /// This initially used standard ELM commands, however the ScanTool family
        /// of devices supports an "STPX" command that simplifies things a lot.
        /// Timeout adjustements are no longer needed, and longer packets are supported.
        /// </remarks>
        public override async Task<bool> SendMessage(Message message)
        {
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                return await this.SendCanMessage(message);
            }

            byte[] messageBytes = message.GetBytes();

            StringBuilder builder = new StringBuilder();
            builder.Append("STPX H:");
            builder.Append(messageBytes[0].ToString("X2"));
            builder.Append(messageBytes[1].ToString("X2"));
            builder.Append(messageBytes[2].ToString("X2"));

            int responses;
            switch (this.TimeoutScenario)
            {
                case TimeoutScenario.DataLogging4:
                    responses = 4;
                    break;

                case TimeoutScenario.DataLogging3:
                    responses = 3;
                    break;

                case TimeoutScenario.DataLogging2:
                    responses = 2;
                    break;

                case TimeoutScenario.DataLogging1:
                    responses = 1;
                    break;

                default:
                    responses = 1;
                    break;
            }

            // Special case for tool-present broadcast messages.
            // TODO: Create a new TimeoutScenario value, maybe call it "TransmitOnly" or something like that.
            if (Utility.CompareArrays(messageBytes, 0x8C, 0xFE, 0xF0, 0x3F))
            {
                responses = 0;
            }

            if (this.TimeoutScenario != TimeoutScenario.DataLoggingStreaming)
            {
                builder.AppendFormat(", R:{0}", responses);
            }

            if (messageBytes.Length < 200)
            {
                // Short messages can be sent with a single write to the ScanTool.
                builder.Append(", D:");
                for(int index = 3; index < messageBytes.Length; index++)
                {
                    builder.Append(messageBytes[index].ToString("X2"));
                }

                string dataResponse = await this.SendRequest(builder.ToString());
                if (!this.ProcessResponse(dataResponse, "STPX with data", allowEmpty: responses == 0))
                {
                    if (dataResponse == string.Empty || dataResponse == "STOPPED" || dataResponse == "?")
                    {
                        // These will happen if the bus is quiet, for example right after uploading the kernel.
                        // They are traced during the SendRequest code. No need to repeat that message.
                    }
                    else
                    {
                        this.Logger.AddUserMessage("Unexpected response to STPX with data: " + dataResponse);
                    }
                    return false;
                }
            }
            else
            {
                // Long messages need to be sent in two steps: first the STPX command, then the data payload.
                builder.Append(", L:");
                int dataLength = messageBytes.Length - 3;
                builder.Append(dataLength.ToString());

                string header = builder.ToString();
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    string headerResponse = await this.SendRequest(header);
                    if (headerResponse != "DATA")
                    {
                        this.Logger.AddUserMessage("Unexpected response to STPX header: " + headerResponse);
                        continue;
                    }

                    break;
                }

                builder = new StringBuilder();
                for (int index = 3; index < messageBytes.Length; index++)
                {
                    builder.Append(messageBytes[index].ToString("X2"));
                }

                string data = builder.ToString();
                string dataResponse = await this.SendRequest(data);

                if (!this.ProcessResponse(dataResponse, "STPX payload", responses == 0))
                {
                    this.Logger.AddUserMessage("Unexpected response to STPX payload: " + dataResponse);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Borrowed from the AllPro class just for testing. Should be removed after STPX is working.
        /// </summary>
        private void ParseMessage(byte[] messageBytes, out string header, out string payload)
        {
            // The incoming byte array needs to separated into header and payload portions,
            // which are sent separately.
            string hexRequest = messageBytes.ToHex();
            header = hexRequest.Substring(0, 9);
            payload = hexRequest.Substring(9);
        }

        /// <summary>
        /// Try to read an incoming message from the device.
        /// </summary>
        public override async Task Receive()
        {
            if (this.CurrentProtocol == BusProtocol.Can500k)
            {
                // CAN responses are normally captured inline by SendCanMessage (an STPX returns the
                // module's reply before its prompt). If the caller reads again after the queue is
                // drained there is nothing pending on the wire, so a read that times out is expected.
                try
                {
                    string canResponse = await this.ReadELMLine();
                    this.ProcessCanResponse(canResponse);
                }
                catch (TimeoutException)
                {
                    this.Logger.AddDebugMessage("Timeout during CAN receive.");
                }
                return;
            }

            try
            {
                string response = await this.ReadELMLine();
                this.ProcessResponse(response, "receive");

                if (this.getRecievedMessageCount!() == 0)
                {
                   // await this.ReceiveViaMonitorMode();
                }
            }
            catch (TimeoutException)
            {
                this.Logger.AddDebugMessage("Timeout during receive.");
                // await this.ReceiveViaMonitorMode();
            }
        }

        /// <summary>
        /// Select the bus this device communicates on. VPW is the default; Can500k switches the STN
        /// to native ISO 15765 at 500 kbaud. Called by the owning <see cref="ElmDevice"/> facade.
        /// </summary>
        public async Task<bool> SetBusProtocol(BusProtocol protocol)
        {
            if (protocol == this.CurrentProtocol)
            {
                return true;
            }

            if (protocol == BusProtocol.Can500k)
            {
                // STP 33 = ISO 15765, 11-bit Tx, 500 kbps, DLC=8. The STN runs the full ISO-TP
                // handshake (First Frame / Flow Control / Consecutive Frames) and reassembly in
                // firmware, so we exchange whole GMLAN payloads - native ISO-TP, no IsoTpTransport.
                if (!await this.SendAndVerify("STP 33", "OK"))
                {
                    this.Logger.AddUserMessage("ScanTool: unable to select ISO 15765 (CAN) protocol.");
                    return false;
                }

                // Auto-formatting on: the device strips/adds the ISO-TP PCI and reassembles multi-frame
                // messages for us. With the GM 11-bit convention (response id = request id + 8) the
                // default flow-control pair and the default receive filter (7E8/7F8) already match the
                // PCM, so no STCFCPA/filter is needed for the standard 7E0/7E8 ids.
                this.Logger.AddDebugMessage(await this.SendRequest("AT CAF1"));

                // Keep adaptive timing OFF (AT AT0, set during initialization) for CAN. The send path
                // doesn't bound the response count, so the device waits the full request timeout for
                // responses. That is what we want: some replies arrive well after a fast exchange would
                // have (the kernel's 0x99 "running" ack comes only once it has booted), and adaptive
                // timing learns the fast timing and stops listening too early - the STN only listens
                // during an STPX, so a reply that lands after it returns is lost. The per-operation
                // timeout (and R:2 for the two-message block read) keep this from being slow where it
                // matters.

                this.currentCanHeader = -1;
                await this.EnsureCanHeader();

                // The CAN send limit is small (see CanMaxMessageSize); the kernel upload divides the
                // image into this many bytes per block, so a small interface still uploads, just in
                // more blocks. Receive needs headroom to reassemble a kernel read block (~1 KB).
                this.MaxSendSize = CanMaxMessageSize;
                this.MaxReceiveSize = 2048 + 12;
                this.Supports4X = false;
                this.CurrentProtocol = BusProtocol.Can500k;
                this.Logger.AddDebugMessage($"ScanTool CAN ready: ISO 15765 500k, tx {this.TxCanId:X3}, rx {this.RxCanId:X3}, native ISO-TP.");
                return true;
            }

            if (protocol == BusProtocol.Vpw)
            {
                // Restore the VPW configuration applied during initialization.
                if (!await this.SendAndVerify("AT SP2", "OK") ||
                    !await this.SendAndVerify("AT DP", "SAE J1850 VPW") ||
                    !await this.SendAndVerify("AT AL", "OK") ||
                    !await this.SendAndVerify("AT H1", "OK") ||
                    !await this.SendAndVerify("AT SR " + DeviceId.Tool.ToString("X2"), "OK"))
                {
                    this.Logger.AddUserMessage("ScanTool: unable to restore VPW protocol.");
                    return false;
                }

                this.MaxSendSize = 1024 + 12;
                this.MaxReceiveSize = 1024 + 12;
                this.CurrentProtocol = BusProtocol.Vpw;
                this.Logger.AddDebugMessage("ScanTool VPW mode restored.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Program the transmit CAN id as the message header (AT SH), but only when it has changed
        /// since the last send, so retargeting a different module takes effect without re-issuing the
        /// command on every frame.
        /// </summary>
        private async Task EnsureCanHeader()
        {
            if (this.currentCanHeader == this.TxCanId)
            {
                return;
            }

            // 11-bit CAN ids use the 3-nibble shorthand form (e.g. 7E0).
            await this.SendRequest("AT SH " + this.TxCanId.ToString("X3"));
            this.currentCanHeader = this.TxCanId;
        }

        /// <summary>
        /// Send a whole GMLAN/UDS payload over CAN. The STN segments it into ISO-TP frames and
        /// drives the flow-control handshake; the reassembled reply (one logical message) is captured
        /// inline and queued for the receive path.
        /// </summary>
        private async Task<bool> SendCanMessage(Message message)
        {
            await this.EnsureCanHeader();

            byte[] bytes = message.GetBytes();
            StringBuilder data = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                data.Append(b.ToString("X2"));
            }

            // The STN only listens to the bus while an STPX is in flight, so the number of responses
            // it is told to collect matters. A kernel block read answers with TWO messages - the 0x75
            // read-ack then the 0x36 data block - so that scenario asks for both (R:2). Every other
            // exchange can answer with a 0x7F..0x78 "response pending" before the real reply, an
            // unpredictable count, so those omit R entirely and let the device collect every response
            // up to the timeout (adaptive timing, enabled for CAN, returns as soon as the bus goes
            // quiet). Bounding those with R:1 was the bug: the STN stopped after the pending frame and
            // never delivered the real answer.
            string responseField = this.TimeoutScenario == TimeoutScenario.ReadMemoryBlock ? ", R:2" : string.Empty;

            string response;
            if (bytes.Length <= 200)
            {
                response = await this.SendRequest("STPX D:" + data + responseField);
            }
            else
            {
                // Long payloads (a kernel upload block) are sent in two steps: the STPX header with a
                // length, then the data after the device prompts "DATA". The device answers the header
                // with "OUT OF MEMORY" (and stays at the prompt) when the message exceeds its CAN
                // buffer, so treat anything but "DATA" as a failed send rather than pushing the payload
                // into a device that isn't expecting it.
                string headerResponse = await this.SendRequest("STPX L:" + bytes.Length + responseField);
                if (headerResponse != "DATA")
                {
                    this.Logger.AddUserMessage(string.Format(
                        "ScanTool CAN: device rejected a {0}-byte message ({1}). It exceeds the interface's CAN buffer.",
                        bytes.Length, string.IsNullOrEmpty(headerResponse) ? "no response" : headerResponse));
                    return false;
                }

                response = await this.SendRequest(data.ToString());
            }

            this.ProcessCanResponse(response);
            return true;
        }

        /// <summary>
        /// Parse the reassembled CAN response blob from the STN into queued messages. With headers on
        /// (AT H1) and spaces off (AT S0), each frame token is the 3-nibble 11-bit id followed by the
        /// reassembled payload (the device has already removed the ISO-TP PCI). The leading id is
        /// stripped so the rest of the stack sees the bare GMLAN payload, like every other CAN device.
        /// </summary>
        private void ProcessCanResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return;
            }

            // ReadELMLine turns each response line's carriage return into a space, so multiple frames
            // arrive as space-separated tokens in one blob.
            string[] tokens = rawResponse.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                // Need at least a 3-nibble id plus one data byte, and the token must be pure hex
                // (skips device chatter like "NO DATA", "STOPPED" or "?").
                if (token.Length < 5 || !token.IsHex())
                {
                    continue;
                }

                uint id = (uint)Convert.ToInt32(token.Substring(0, 3), 16);
                if (id != this.RxCanId)
                {
                    // Not from the target (e.g. a transmit echo on the request id); ignore it.
                    continue;
                }

                byte[] payload = token.Substring(3).ToBytes();
                if (payload.Length == 0)
                {
                    continue;
                }

                this.Logger.AddDebugMessage("RX: " + payload.ToHex());
                this.enqueue!(new Message(payload));
            }
        }

        public override async Task<bool> IsCommandBroadcasting(byte command)
        {
            await this.SendNoReply("STM");
            await Task.Delay(400);
            await this.SendNoReply("\r");
            string monitorResponse = await this.ReadELMLine();
            string testString = $"{Priority.Physical0:X2}{DeviceId.Tool:X2}{DeviceId.Pcm:X2}{command:X2}";
            this.Logger.AddDebugMessage("Response to STM: " + monitorResponse);
            if (monitorResponse.Contains(testString))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// This doesn't actually work yet, but I like the idea...
        /// </summary>
        private async Task ReceiveViaMonitorMode()
        {
            try
            {
                string monitorResponse = await this.SendRequest("AT MA");
                this.Logger.AddDebugMessage("Response to AT MA 1: " + monitorResponse);

                if (monitorResponse != ">?")
                {
                    string response = await this.ReadELMLine();
                    this.ProcessResponse(monitorResponse, "receive via monitor");
                }
            }
            catch(TimeoutException)
            {
                this.Logger.AddDebugMessage("Timeout during receive via monitor mode.");
            }
            finally
            { 
                string stopMonitorResponse = await this.SendRequest("AT MA");
                this.Logger.AddDebugMessage("Response to AT MA 2: " + stopMonitorResponse);
            }
        }
    }
}

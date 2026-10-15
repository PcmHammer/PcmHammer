// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class LogStartFailedException : Exception
    {
        public LogStartFailedException() : base("Unable to start logging.")
        { }

        public LogStartFailedException(string message) : base(message)
        { }
    }

    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    public partial class Vehicle : IDisposable
    {
        /// <summary>
        /// Builds the GMLAN messages used when the module being logged is on CAN.
        /// </summary>
        private readonly Gmlan gmlan = new Gmlan();

        /// <summary>
        /// Create a logger.
        /// </summary>
        /// <remarks>
        /// Varies with the device's capability and with the bus the module was found on: DPIDs are a
        /// VPW mechanism, so a CAN module is polled instead.
        /// </remarks>
        public Logger CreateLogger(
            uint osid,
            IEnumerable<LogColumn> columns,
            ILogger uiLogger)
        {
            return Logger.Create(
                this,
                osid,
                columns,
                this.device.SupportsSingleDpidLogging,
                this.device.SupportsStreamLogging,
                this.LastDetectedBus ?? BusProtocol.VPW,
                uiLogger);
        }

        /// <summary>
        /// Read one parameter from the selected CAN module (GMLAN service 0x22), one PID per
        /// exchange.
        /// </summary>
        public async Task<Response<byte[]>> ReadCanParameter(ushort pid)
        {
            Message request = this.gmlan.CreateParameterRequest(pid);
            if (!await this.device.SendMessage(request))
            {
                return Response.Create(ResponseStatus.Error, Array.Empty<byte>());
            }

            // Several reads: the device echoes its own transmit frame, and an earlier timeout can
            // leave a late reply in the queue.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Message responseMessage = await this.ReceiveMessage();
                if (responseMessage == null)
                {
                    break;
                }

                Response<byte[]> parsed = this.gmlan.ParseParameterResponse(responseMessage, pid);
                if (parsed.Status == ResponseStatus.Success ||
                    parsed.Status == ResponseStatus.Refused ||
                    parsed.Status == ResponseStatus.Error)
                {
                    return parsed;
                }
            }

            return Response.Create(ResponseStatus.Timeout, Array.Empty<byte>());
        }

        /// <summary>
        /// Prepare the PCM to begin sending collections of parameters.
        /// </summary>
        /// <summary>
        /// Prepare the PCM to begin sending collections of parameters. Parameters the PCM refuses are
        /// added to <paramref name="unsupported"/> and dropped from the configuration rather than
        /// ending the session.
        /// </summary>
        /// <remarks>
        /// Refusing to log at all because one parameter is unavailable made a long parameter list
        /// unusable: the list offers far more than any single operating system implements, so picking
        /// an unlucky one meant nothing logged and a stack trace to interpret. Dropping it keeps the
        /// rest of the profile working, which is what the user asked for.
        ///
        /// Safe to drop mid-configuration because position is only advanced for parameters the PCM
        /// accepted, and the refused column is removed from the group afterwards - so what the row
        /// parser expects and what the PCM sends stay in step.
        /// </remarks>
        public async Task<DpidCollection> ConfigureDpids(
            DpidConfiguration dpidConfiguration, uint osid, List<Parameter> unsupported)
        {
            List<byte> dpids = new List<byte>();

            await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);

            // If we were logging, the queue might be full of incoming data.
            this.ClearDeviceMessageQueue();

            // This seemed like a good idea, but it causes "service vehicle soon" on the dash.
            // Message suppressChatter = this.protocol.CreateDisableNormalMessageTransmission();
            // await this.SendMessage(suppressChatter);

            foreach (ParameterGroup group in dpidConfiguration.ParameterGroups)
            {
                int position = 1;
                List<LogColumn> refused = new List<LogColumn>();

                // ToList: the group's columns are edited below once the PCM has had its say.
                foreach (LogColumn column in group.LogColumns.ToList())
                {
                    PidParameter? pidParameter = column.Parameter as PidParameter;
                    RamParameter? ramParameter = column.Parameter as RamParameter;
                    int byteCount;

                    if (pidParameter != null)
                    {
                        Message configurationMessage = this.protocol.ConfigureDynamicData(
                            (byte)group.Dpid,
                            DefineBy.Pid,
                            position,
                            pidParameter.ByteCount,
                            pidParameter.PID);

                        // Response parsing happens further below.
                        if (!await this.SendMessage(configurationMessage))
                        {
                            throw new LogStartFailedException("Unable to send DPID configuration request.");
                        }

                        byteCount = pidParameter.ByteCount;
                    }
                    else if (ramParameter != null)
                    {
                        uint address;
                        if (ramParameter.TryGetAddress(osid, out address))
                        {
                            Message configurationMessage = this.protocol.ConfigureDynamicData(
                                (byte)group.Dpid,
                                DefineBy.Address,
                                position,
                                ramParameter.ByteCount,
                                address);

                            // Response parsing happens further below.
                            if (!await this.SendMessage(configurationMessage))
                            {
                                throw new LogStartFailedException("Unable to send DPID configuration request.");
                            }

                            byteCount = ramParameter.ByteCount;
                        }
                        else
                        {
                            logger.AddUserMessage(
                                string.Format("Parameter {0} is not defined for PCM {1}",
                                ramParameter.Name,
                                osid));
                            byteCount = 0;
                        }
                    }
                    else
                    {
                        throw new LogStartFailedException(
                            $"Why does this ParameterGroup contain a {column.Parameter.GetType().Name}? See {column.Parameter.Name}.");
                    }

                    // Wait for a success or fail message.
                    // TODO: move this into the protocol layer.
                    bool configured = false;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Message responseMessage = await this.ReceiveMessage();

                        if (responseMessage == null)
                        {
                            continue;
                        }

                        if (responseMessage.Length < 5)
                        {
                            continue;
                        }

                        if (responseMessage[3] == 0x6C)
                        {
                            logger.AddDebugMessage("Configured " + column.ToString());
                            configured = true;
                            break;
                        }

                        if (responseMessage[3] == 0x7F && responseMessage[4] == 0x2C)
                        {
                            // This PCM does not implement this parameter. Drop it and keep going.
                            logger.AddUserMessage(
                                column.Parameter.Name + " is not available on this PCM, so it will not be logged.");
                            unsupported.Add(column.Parameter);
                            refused.Add(column);
                            break;
                        }
                    }

                    if (refused.Contains(column))
                    {
                        // Contributed no bytes, so the next parameter takes this one's position.
                        continue;
                    }

                    if (!configured)
                    {
                        throw new ApplicationException("Unable to request parameter: " + column.ToString());
                    }

                    position += byteCount;
                }

                foreach (LogColumn column in refused)
                {
                    group.LogColumns.Remove(column);
                }

                // A group whose every parameter was refused was never configured, so the PCM must not
                // be asked for it.
                if (group.LogColumns.Count > 0)
                {
                    dpids.Add((byte)group.Dpid);
                }
            }

            dpidConfiguration.ParameterGroups.RemoveAll(g => g.LogColumns.Count == 0);

            return new DpidCollection(dpids.ToArray());
        }

        /// <summary>
        /// Begin data logging.
        /// </summary>
        /// <remarks>
        /// In the future we could make "bool streaming" into an enum, with
        /// Fast, Slow, and  Mixed options. Mixed mode would request some 
        /// parameters at 10hz and others at 5hz.
        /// 
        /// This would require the user to specify, or the app to just know,
        /// which parameters to poll at 5hz rather than 10hz. A list of
        /// 5hz-friendly parameters is not out of the question. Some day.
        /// 
        /// The PCM always sends an error response to these messages, so
        /// we just ignore responses in all cases.
        /// </remarks>
        public async Task<bool> RequestDpids(DpidCollection dpids, bool streaming)
        {
            if (streaming)
            {
                // Request all of the parameters at 5hz using stream 1.
                Message step1 = this.protocol.RequestDpids(dpids, Protocol.DpidRequestType.Stream1);
                if (!await this.SendMessage(step1))
                {
                    return false;
                }

                // Request all of the parameters at 5hz using stream 2. Now we get them all at 10hz.
                Message step2 = this.protocol.RequestDpids(dpids, Protocol.DpidRequestType.Stream2);
                if (!await this.SendMessage(step2))
                {
                    return false;
                }
            }
            else
            {
                // Request one row of data.
                Message startMessage = this.protocol.RequestDpids(dpids, Protocol.DpidRequestType.SingleRow);
                if (!await this.SendMessage(startMessage))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tell the PCM to stop streaming, and drop whatever it has already sent.
        /// </summary>
        /// <remarks>
        /// A stream runs until it is stopped, so pausing our end is not enough: anything else that
        /// wants the bus - reading trouble codes, say - otherwise reads log rows instead of answers.
        /// </remarks>
        public async Task<bool> StopDpidStream(DpidCollection dpids)
        {
            Message halt = this.protocol.RequestDpids(dpids, Protocol.DpidRequestType.Halt);
            bool sent = await this.SendMessage(halt);

            this.ClearDeviceMessageQueue();
            return sent;
        }

        /// <summary>
        /// Read a dpid response from the PCM.
        /// </summary>
        /// <remarks>
        /// Intentionally not wrapped in an inbound filter: this consumes a continuous DPID
        /// data stream that an earlier RequestDpids set running, so there is no paired request
        /// here to derive a filter from, and filtering a stream could drop log rows. Like the
        /// recovery-mode listens, it stays unfiltered.
        /// </remarks>
        public async Task<RawLogData?> ReadLogData()
        {
            Message message;
            RawLogData? result = null;

            for (int attempt = 1; attempt < 5; attempt++)
            {
                message = await this.ReceiveMessage();
                if (message == null)
                {
                    break;
                }
                
                if (this.protocol.TryParseRawLogData(message, out result))
                {
                    break;
                }
            } 

            return result;
        }

        /// <summary>
        /// This is needed to keep streaming logging active.
        /// </summary>
        public async Task SendDataLoggerPresentNotification()
        {
            Message message = this.protocol.CreateDataLoggerPresentNotification();
            await this.device.SendMessage(message);
        }

        /// <summary>
        /// Currently only used by VpwExplorer for testing.
        /// </summary>
        /// <param name="pid"></param>
        /// <returns></returns>
        public async Task<Response<int>> GetPid(UInt32 pid)
        {
            Message request = this.protocol.CreatePidRequest(pid);
            if(!await this.TrySendMessage(request, "PID request"))
            {
                return Response.Create(ResponseStatus.Error, 0);
            }

            // Single-target request/response, so filter to the PCM's reply: only one receive
            // happens here, so dropping off-conversation traffic at the device keeps that one
            // read from grabbing unrelated bus noise.
            using (this.device.FilterInbound(MessageFilters.RepliesFrom(request)))
            {
                Message responseMessage = await this.ReceiveMessage();
                if (responseMessage == null)
                {
                    return Response.Create(ResponseStatus.Error, 0);
                }

                return this.protocol.ParsePidResponse(responseMessage);
            }
        }

        public async Task<Response<uint>> GetRam(int address)
        {
            Query<uint> query = new Query<uint>(
                this.device,
                () => this.protocol.CreateRamRequest(address),
                this.protocol.ParseRamResponse,
                this.logger,
                CancellationToken.None);

            return await query.Execute();
        }

        /// <summary>
        /// For historical reference only.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> StartLogging_Old()
        {
            // NOT SUPPORTED (in my ROM anyway, need to try others)
            // 19F3 - transmission temprature
            // 1602 - oil temperature
            // 125D - knock retard
            // 19F5 - current gear
            // 125E - knock count, two-byte

            // Configure logging parameters
            // DPID numbers 0xF2-0xFE all work
            // 0xFE is highest priority
            // 0xFA is very slow
            byte dpid1 = 0xFE;


            // Load 2 bytes of SAE RPM to DPID positions 1 and 2
            Message message = this.protocol.ConfigureDynamicData(dpid1, DefineBy.Pid, 1, 2, 0x000C);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 2 bytes from SAE MAF to positions 3 and 4
            message = this.protocol.ConfigureDynamicData(dpid1, DefineBy.Pid, 3, 2, 0x0010);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of SAE MAP to position 5
            message = this.protocol.ConfigureDynamicData(dpid1, DefineBy.Pid, 5, 1, 0x000B);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of SAE TPS to position 6
            message = this.protocol.ConfigureDynamicData(dpid1, DefineBy.Pid, 6, 1, 0x0011);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            byte dpid2 = 0xFD;

            // Load SAE IAT to DPID position 1
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 1, 1, 0x000F);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // Load SAE coolant temperature to position 2
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 2, 1, 0x0005);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of SAE trans temp to position 3 (this is raw sensor value.)
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 3, 1, 0x19AD);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of SAE speed (KMH) to position 4 
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 4, 1, 0x000D);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of GM Knock to position 5
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 5, 1, 0x11A6);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of GM fuel status to position 6 
            message = this.protocol.ConfigureDynamicData(dpid2, DefineBy.Pid, 6, 1, 0x1105);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            /*
            byte dpid3 = 0xFC;

            // Load left LTFT to position 1
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 1, 1, 0x0007);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // Load right LTFT to position 2
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 2, 1, 0x0009);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of Gm Target AFR to position 3 (0:1-25.5:1)
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 3, 1, 0x119E);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of GM battery voltage to position 4
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 4, 1, 0x1141);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of GM current BLM cell to position 5 
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 5, 1, 0x1190);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            // load 1 byte of "normalized TPS" (so what's the other TPS parmeter?) to position 6
            message = this.protocol.ConfigureDynamicData(dpid3, DefineBy.Pid, 6, 1, 0x1151);
            if (!await this.SendMessage(message))
            {
                return false;
            }
            */

            // Start logging
            //message = this.protocol.BeginLogging(dpid1, dpid2);//, dpid3);
            if (!await this.SendMessage(message))
            {
                return false;
            }

            return true;
        }
    }
}

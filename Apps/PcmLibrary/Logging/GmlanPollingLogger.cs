// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Logs a CAN module by asking for one parameter at a time (GMLAN service 0x22).
    /// </summary>
    /// <remarks>
    /// DPIDs are a VPW mechanism: the configuration message carries a VPW header, which a CAN module
    /// reads as the service id and rejects. Answers are assembled into DPID-shaped rows anyway so the
    /// row parser, math columns and dashboard are unchanged - the parameter groups are a row layout
    /// here, nothing on the wire knows about them. Slowest of the three loggers, but the only one
    /// needing no setup; GMLAN's periodic-data service would replace this class alone.
    /// </remarks>
    public class GmlanPollingLogger : Logger
    {
        public GmlanPollingLogger(
            Vehicle vehicle,
            uint osid,
            DpidConfiguration dpidConfiguration,
            MathValueProcessor mathValueProcessor,
            ILogger uiLogger)
            : base(
                  vehicle,
                  osid,
                  dpidConfiguration,
                  mathValueProcessor,
                  uiLogger)
        {
        }

        /// <summary>
        /// Nothing to configure, so ask for each parameter once instead: a PID this module lacks
        /// would otherwise be re-requested and re-refused on every row.
        /// </summary>
        protected override async Task<bool> ConfigureParameters()
        {
            await this.Vehicle.SetDeviceTimeout(TimeoutScenario.ReadProperty);

            bool anyAnswered = false;

            foreach (ParameterGroup group in this.DpidConfiguration.ParameterGroups)
            {
                // ToList: the group's columns are edited below.
                foreach (LogColumn column in group.LogColumns.ToList())
                {
                    if (!(column.Parameter is PidParameter pidParameter))
                    {
                        // RAM parameters are read by address, which on CAN is a different service
                        // (0x23) and a different address map.
                        this.UILogger.AddUserMessage(
                            column.Parameter.Name + " cannot be read from a CAN module yet, so it will not be logged.");
                        this.MarkUnsupported(column.Parameter);
                        group.LogColumns.Remove(column);
                        continue;
                    }

                    Response<byte[]> response = await this.Vehicle.ReadCanParameter((ushort)pidParameter.PID);

                    switch (response.Status)
                    {
                        case ResponseStatus.Success:
                            anyAnswered = true;
                            continue;

                        case ResponseStatus.Refused:
                            this.UILogger.AddUserMessage(
                                column.Parameter.Name + " is not available on this PCM, so it will not be logged.");
                            this.MarkUnsupported(column.Parameter);
                            group.LogColumns.Remove(column);
                            continue;

                        case ResponseStatus.Error:
                            throw new LoggingNotSupportedException(
                                "This PCM does not support reading parameters over CAN with service 0x22. " +
                                "Logging a module on CAN needs the GMLAN periodic-data service, which is not implemented yet.");

                        default:
                            // No answer could be a busy module rather than a missing parameter, so
                            // keep it - a short row is retried, and the user can un-tick it.
                            this.UILogger.AddDebugMessage(
                                $"No answer for {column.Parameter.Name} (PID {pidParameter.PID:X4}) while probing.");
                            continue;
                    }
                }
            }

            this.DpidConfiguration.ParameterGroups.RemoveAll(g => g.LogColumns.Count == 0);

            if (!anyAnswered && this.DpidConfiguration.ParameterGroups.Count > 0)
            {
                throw new LogStartFailedException("This PCM did not answer any parameter request on CAN.");
            }

            return true;
        }

        protected override async Task<bool> StartLoggingInternal()
        {
            // Request/response, so the plain request timeout - the data-logging scenarios size their
            // waits for an unsolicited DPID stream.
            await this.Vehicle.SetDeviceTimeout(TimeoutScenario.ReadProperty);
            return true;
        }

        protected override async Task GetNextRowInternal(LogRowParser row)
        {
            foreach (ParameterGroup group in this.DpidConfiguration.ParameterGroups)
            {
                byte[] payload = new byte[ParameterGroup.MaxBytes];
                int position = 0;

                foreach (LogColumn column in group.LogColumns)
                {
                    if (!(column.Parameter is PidParameter pidParameter))
                    {
                        continue;
                    }

                    Response<byte[]> response = await this.Vehicle.ReadCanParameter((ushort)pidParameter.PID);
                    if (response.Status != ResponseStatus.Success)
                    {
                        // Abandon the row rather than publish a partly-stale one.
                        return;
                    }

                    byte[] value = response.Value;
                    int count = Math.Min(pidParameter.ByteCount, value.Length);
                    if (position + count <= payload.Length)
                    {
                        Array.Copy(value, 0, payload, position, count);
                    }

                    // By the declared width, not by what the module sent: the row parser reads back
                    // at the same running offset.
                    position += pidParameter.ByteCount;
                }

                row.ParseData(new RawLogData(group.Dpid, payload));
            }
        }
    }
}

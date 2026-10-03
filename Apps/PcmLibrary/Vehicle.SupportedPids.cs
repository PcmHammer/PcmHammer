// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class Vehicle
    {
        /// <summary>
        /// Ask the module which generic PIDs it supports.
        /// </summary>
        /// <remarks>
        /// Returns null when the question could not be answered, which is different from an empty
        /// set: null means "no idea, assume everything works", where empty would mean "supports
        /// nothing". Older modules do not implement this and some answer with rubbish, and either
        /// way the app must not start switching parameters off on the strength of it.
        ///
        /// Only the generic range is covered. The manufacturer's own PIDs have no support mask, so
        /// they are absent from the result whether or not the module has them - a caller must not
        /// read their absence as unsupported.
        /// </remarks>
        public async Task<HashSet<uint>?> ReadSupportedPids(CancellationToken cancellationToken)
        {
            await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);
            this.ClearDeviceMessageQueue();

            if (this.LastDetectedBus == BusProtocol.Can500k)
            {
                // Not implemented for CAN: the generic service is not how a CAN PCM is polled here,
                // and guessing would switch off parameters that work.
                return null;
            }

            // The replies are functionally addressed, like the trouble-code lists, so the usual
            // filters would drop them.
            await this.device.SetBusFilters(BusFilters.VPWDiagnostics);

            try
            {
                HashSet<uint> supported = new HashSet<uint>();
                bool anyBlockAnswered = false;

                foreach (byte block in Protocol.SupportedPidBlocks)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    uint? mask = await this.ReadSupportedPidBlock(block);
                    if (mask == null)
                    {
                        break;
                    }

                    anyBlockAnswered = true;

                    for (uint pid = (uint)block + 1; pid <= block + 32; pid++)
                    {
                        if (Protocol.MaskIncludes(block, mask.Value, pid))
                        {
                            supported.Add(pid);
                        }
                    }

                    if (!Protocol.MaskPromisesNextBlock(block, mask.Value))
                    {
                        break;
                    }
                }

                if (!anyBlockAnswered)
                {
                    this.logger.AddDebugMessage("The PCM did not report which PIDs it supports.");
                    return null;
                }

                // A module that answers but claims nothing is not telling the truth about itself.
                // Treated as no answer, because the alternative is an empty parameter list.
                if (supported.Count == 0)
                {
                    this.logger.AddDebugMessage("The PCM reported an empty PID list; ignoring it.");
                    return null;
                }

                this.logger.AddUserMessage($"The PCM supports {supported.Count} generic PIDs.");
                return supported;
            }
            finally
            {
                await this.device.RestoreBusFilters();
            }
        }

        private async Task<uint?> ReadSupportedPidBlock(byte block)
        {
            if (!await this.device.SendMessage(this.protocol.CreateSupportedPidsRequest(block)))
            {
                return null;
            }

            // Several reads: the request is functional, so other modules may answer first.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Message response = await this.ReceiveMessage();
                if (response == null)
                {
                    return null;
                }

                if (this.protocol.TryParseSupportedPids(response, block, out uint mask))
                {
                    return mask;
                }
            }

            return null;
        }
    }
}

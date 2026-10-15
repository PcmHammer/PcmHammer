// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public partial class Vehicle
    {
        /// <summary>
        /// Read the stored, pending and permanent codes from the selected module.
        /// </summary>
        /// <remarks>
        /// All three lists, because which one a code is in is most of what it tells you: a pending
        /// code is a hint, a stored one has lit the lamp, and a permanent one will not clear until
        /// the module is satisfied the fault is gone.
        ///
        /// A list that returns nothing is not an error. Modules routinely answer one service and
        /// refuse another, and "no pending codes" and "pending codes not supported" look the same
        /// from here - neither is worth stopping for.
        /// </remarks>
        public async Task<IReadOnlyList<DiagnosticCode>> ReadTroubleCodes(
            DiagnosticCodeDefinitions definitions, CancellationToken cancellationToken)
        {
            List<DiagnosticCode> found = new List<DiagnosticCode>();

            await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);

            // Anything left from whatever was using the bus before this.
            this.ClearDeviceMessageQueue();

            bool onVPW = this.LastDetectedBus != BusProtocol.Can500k;

            if (onVPW)
            {
                // The manufacturer's own service first: it reports everything the module holds,
                // where the generic services only admit to emissions codes that have matured. Its
                // replies come back to the tool, so the usual filters already carry them.
                IReadOnlyList<DiagnosticCode>? reported = await this.ReadTroubleCodesByStatus(definitions);
                if (reported != null)
                {
                    return reported;
                }

                // Widened for this operation only, then put back: the answers to these questions
                // are not addressed to the tool, so the filters the rest of the app runs with
                // exclude them. CAN carries no header to filter on, so this is VPW's problem only.
                if (!await this.device.SetBusFilters(BusFilters.VPWDiagnostics))
                {
                    // Said out loud, because an interface that could not widen will report no codes
                    // whether or not there are any, and that is worth distrusting.
                    this.logger.AddUserMessage(
                        "This interface could not listen for the replies to these questions, " +
                        "so codes may be missed.");
                }
            }

            try
            {
                foreach (DiagnosticCodeKind kind in
                    new[] { DiagnosticCodeKind.Stored, DiagnosticCodeKind.Pending, DiagnosticCodeKind.Permanent })
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    IReadOnlyList<ushort> raw = this.LastDetectedBus == BusProtocol.Can500k
                        ? await this.ReadTroubleCodesOverCan(kind)
                        : await this.ReadTroubleCodesOverVPW(kind);

                    foreach (ushort code in raw)
                    {
                        // The same code can be both stored and permanent, and both are worth
                        // showing; the same code twice in one list is padding, and is not.
                        if (!found.Any(x => x.Raw == code && x.Kind == kind))
                        {
                            found.Add(definitions.Describe(code, kind));
                        }
                    }
                }
            }
            finally
            {
                if (onVPW)
                {
                    await this.device.RestoreBusFilters();
                }
            }

            return found;
        }

        /// <summary>
        /// Read the codes through the manufacturer's status-mask service, or null if the module
        /// does not offer it - in which case the caller falls back to the generic services.
        /// </summary>
        /// <remarks>
        /// One request. The module sends a message per code and ends with a terminator, so an empty
        /// list is a single reply - there is nothing to gain by asking for a count first.
        /// </remarks>
        private async Task<IReadOnlyList<DiagnosticCode>?> ReadTroubleCodesByStatus(
            DiagnosticCodeDefinitions definitions)
        {
            if (!await this.device.SendMessage(this.protocol.CreateTroubleCodeListRequest()))
            {
                return null;
            }

            List<DiagnosticCode> found = new List<DiagnosticCode>();
            bool complete = false;

            // Bounded only against a module that never terminates; a receive timeout ends it first.
            for (int attempt = 0; attempt < 300 && !complete; attempt++)
            {
                Message response = await this.ReceiveMessage();
                if (response == null)
                {
                    break;
                }

                if (this.protocol.IsTroubleCodeStatusRefusal(response))
                {
                    this.logger.AddDebugMessage("This PCM does not support reading codes by status.");
                    return null;
                }

                if (!this.protocol.IsTroubleCodeStatusReply(response))
                {
                    continue;
                }

                if (!this.protocol.TryParseTroubleCodeByStatus(response, out ushort code, out byte status))
                {
                    // The terminator, so the module has nothing further to send.
                    complete = true;
                    break;
                }

                if (!found.Any(x => x.Raw == code))
                {
                    found.Add(new DiagnosticCode(
                        code,
                        DiagnosticCodeKind.Reported,
                        definitions.DescriptionFor(DiagnosticCode.Decode(code)),
                        status));
                }
            }

            if (!complete)
            {
                // Whatever is still coming would otherwise be read as an answer to the next thing
                // asked, which is how a list of codes turns into a mystery.
                this.logger.AddDebugMessage("The code list did not finish; discarding what follows.");
                this.ClearDeviceMessageQueue();
                return found.Count > 0 ? found : null;
            }

            return found;
        }

        private async Task<IReadOnlyList<ushort>> ReadTroubleCodesOverVPW(DiagnosticCodeKind kind)
        {
            Message request = this.protocol.CreateTroubleCodeRequest(kind);
            if (!await this.device.SendMessage(request))
            {
                return Array.Empty<ushort>();
            }

            List<ushort> codes = new List<ushort>();

            // Several reads: the request is functional, so more than one module may answer, and a
            // module with more codes than fit in one message sends several.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Message response = await this.ReceiveMessage();
                if (response == null)
                {
                    break;
                }

                TroubleCodeReply reply = this.protocol.ParseTroubleCodes(
                    response, kind, out byte source, out List<ushort> batch);

                if (reply == TroubleCodeReply.Refused)
                {
                    // Said once rather than per service: three refusals in a row is one fact.
                    this.logger.AddDebugMessage(
                        $"Module {source:X2} does not support service {Protocol.TroubleCodeMode(kind):X2}.");

                    // Definitive, so there is nothing to wait for - and waiting costs a full
                    // receive timeout per service.
                    break;
                }

                if (reply == TroubleCodeReply.Codes)
                {
                    codes.AddRange(batch);
                }
            }

            return codes;
        }

        private async Task<IReadOnlyList<ushort>> ReadTroubleCodesOverCan(DiagnosticCodeKind kind)
        {
            Message request = this.gmlan.CreateTroubleCodeRequest(kind);
            if (!await this.device.SendMessage(request))
            {
                return Array.Empty<ushort>();
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                Message response = await this.ReceiveMessage();
                if (response == null)
                {
                    break;
                }

                Response<List<ushort>> parsed = this.gmlan.ParseTroubleCodes(response, kind);
                if (parsed.Status == ResponseStatus.Success)
                {
                    return parsed.Value;
                }

                if (parsed.Status == ResponseStatus.Error)
                {
                    // Refused. Nothing more to wait for on this service.
                    break;
                }
            }

            return Array.Empty<ushort>();
        }

        /// <summary>
        /// Clear the stored codes. Permanent codes are not affected - only the module clears those.
        /// </summary>
        public async Task<bool> ClearTroubleCodes(CancellationToken cancellationToken)
        {
            await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);

            Message request = this.LastDetectedBus == BusProtocol.Can500k
                ? this.gmlan.CreateClearDiagnosticsRequest()
                : this.protocol.CreateClearDiagnosticTroubleCodesRequest();

            return await this.device.SendMessage(request);
        }
    }
}

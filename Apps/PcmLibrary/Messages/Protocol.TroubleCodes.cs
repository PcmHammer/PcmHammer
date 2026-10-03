// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>What a message turned out to be when asking for trouble codes.</summary>
    public enum TroubleCodeReply
    {
        /// <summary>Not an answer to this request. Keep reading.</summary>
        Unrelated,

        /// <summary>A module listed its codes, possibly none.</summary>
        Codes,

        /// <summary>A module does not implement the service.</summary>
        Refused,
    }

    public partial class Protocol
    {
        /// <summary>Functional address for the emissions-related modules.</summary>
        private const byte EmissionsModules = 0x6A;

        /// <summary>
        /// The same functional address as it appears in a reply, which is where the answers go
        /// rather than to the tool that asked. The clear-request handling alongside uses it too.
        /// </summary>
        private const byte EmissionsModulesReply = 0x6B;

        /// <summary>
        /// The service that reports each list of codes.
        /// </summary>
        public static byte TroubleCodeMode(DiagnosticCodeKind kind)
        {
            switch (kind)
            {
                case DiagnosticCodeKind.Pending:
                    return Mode.GetPendingTroubleCodes;

                case DiagnosticCodeKind.Permanent:
                    return Mode.GetPermanentTroubleCodes;

                default:
                    return Mode.GetStoredTroubleCodes;
            }
        }

        /// <summary>
        /// Ask for a list of codes.
        /// </summary>
        /// <remarks>
        /// Functionally addressed, like the clear request beside it. The generic OBD-II services are
        /// answered on the emissions address rather than at a module's own: asked physically, a PCM
        /// that supports them perfectly well replies "service not supported".
        /// </remarks>
        public Message CreateTroubleCodeRequest(DiagnosticCodeKind kind) =>
            new Message(new byte[]
            {
                Priority.Functional0, EmissionsModules, ToolId, TroubleCodeMode(kind),
            });

        /// <summary>
        /// Work out what a reply is, and read any codes out of it.
        /// </summary>
        /// <remarks>
        /// An answer to a functional request is itself functionally addressed - it is not sent back
        /// to the tool that asked - so the destination is the emissions address rather than the tool
        /// id. A refusal comes back physically addressed, so both are accepted here.
        ///
        /// Any module may answer, so the source is reported rather than required to match. The
        /// payload is pairs of bytes after the response service id, zero-padded to three pairs.
        /// </remarks>
        public TroubleCodeReply ParseTroubleCodes(
            Message message, DiagnosticCodeKind kind, out byte source, out List<ushort> codes)
        {
            codes = new List<ushort>();
            source = 0;

            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
            byte requested = TroubleCodeMode(kind);

            if (bytes.Length < 4 ||
                (bytes[1] != EmissionsModulesReply && bytes[1] != ToolId))
            {
                return TroubleCodeReply.Unrelated;
            }

            source = bytes[2];

            if (bytes[3] == Mode.NegativeResponse)
            {
                return bytes.Length > 4 && bytes[4] == requested
                    ? TroubleCodeReply.Refused
                    : TroubleCodeReply.Unrelated;
            }

            if (bytes[3] != (byte)(requested + Mode.Response))
            {
                return TroubleCodeReply.Unrelated;
            }

            ReadCodePairs(bytes, 4, codes);
            return TroubleCodeReply.Codes;
        }

        /// <summary>
        /// Which status bits make a fault worth reporting as a trouble code.
        /// </summary>
        /// <remarks>
        /// The module ANDs this against each enabled fault's status and reports any fault with a
        /// bit in common, so the mask has to exclude everything that is not a failure:
        ///
        ///   bit 0  active prior trip   - set on every enabled fault, so 0xFF reports the whole
        ///                                supported-code table rather than the faults
        ///   bit 1  fault active now            * reported
        ///   bit 2  test not passed since clear - a monitor that has not run, not a failure
        ///   bit 3  test failed since clear     * reported
        ///   bit 4  history                     * reported
        ///   bit 5  test not passed this key-on - likewise not a failure
        ///   bit 6  test failed this key-on     * reported
        ///   bit 7  lamp requested              * reported
        /// </remarks>
        public const byte TroubleCodeFailureMask = 0xDA;

        /// <summary>
        /// Ask for the codes, which arrive one per message and end with a terminator.
        /// </summary>
        /// <remarks>
        /// The manufacturer's service, so physically addressed - its replies come back to the tool
        /// and need no change to the usual filters.
        ///
        /// The trailing 0xFF 0x00 selects the list. Trailing 0xFF 0xFF would ask for a count
        /// instead, which is not worth a round trip: an empty list costs one reply either way.
        /// Anything else is refused as an unsupported subfunction, which is what happens to a
        /// tester that tries the UDS form of this service on this module.
        ///
        /// The module queues the next reply after each code it sends, so once this is asked the
        /// list runs to its terminator whether or not anyone is still reading. Abandoning it early
        /// leaves the codes arriving underneath whatever is asked next.
        /// </remarks>
        public Message CreateTroubleCodeListRequest() =>
            new Message(new byte[]
            {
                Priority.Physical0, TargetVPWId, ToolId, Mode.GetTroubleCodesByStatus,
                TroubleCodeFailureMask, 0xFF, 0x00,
            });

        /// <summary>
        /// Spell out a status byte from the mask service.
        /// </summary>
        /// <remarks>
        /// Bit 0 is left out: the module sets it on every enabled fault, so it says nothing.
        /// </remarks>
        public static string DescribeTroubleCodeStatus(byte status)
        {
            List<string> parts = new List<string>();

            if ((status & 0x02) != 0) { parts.Add("failing now"); }
            if ((status & 0x80) != 0) { parts.Add("lamp on"); }
            if ((status & 0x10) != 0) { parts.Add("history"); }
            if ((status & 0x40) != 0) { parts.Add("failed this key-on"); }
            if ((status & 0x08) != 0) { parts.Add("failed since last clear"); }
            if ((status & 0x04) != 0) { parts.Add("not run since last clear"); }
            if ((status & 0x20) != 0) { parts.Add("not run this key-on"); }

            return parts.Count == 0 ? "enabled" : string.Join(", ", parts);
        }

        /// <summary>
        /// Read one code out of a reply to <see cref="CreateTroubleCodeListRequest"/>. False at the
        /// end of the list, which the module marks with a code of zero and a status of 0xFF.
        /// </summary>
        public bool TryParseTroubleCodeByStatus(Message message, out ushort code, out byte status)
        {
            code = 0;
            status = 0;

            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
            if (!IsStatusReply(bytes) || bytes.Length != 7)
            {
                return false;
            }

            code = (ushort)((bytes[4] << 8) | bytes[5]);
            status = bytes[6];

            return code != 0;
        }

        /// <summary>Whether a message is a reply to the status-mask service, addressed to us.</summary>
        public bool IsTroubleCodeStatusReply(Message message) =>
            IsStatusReply(message?.GetBytes() ?? Array.Empty<byte>());

        private bool IsStatusReply(byte[] bytes) =>
            bytes.Length >= 4 &&
            bytes[1] == ToolId &&
            bytes[3] == (byte)(Mode.GetTroubleCodesByStatus + Mode.Response);

        /// <summary>Whether a message refuses the status-mask service.</summary>
        public bool IsTroubleCodeStatusRefusal(Message message)
        {
            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();

            return bytes.Length >= 5 &&
                bytes[1] == ToolId &&
                bytes[3] == Mode.NegativeResponse &&
                bytes[4] == Mode.GetTroubleCodesByStatus;
        }

        /// <summary>
        /// Collect the two-byte codes from a payload, skipping the all-zero padding pairs.
        /// </summary>
        public static void ReadCodePairs(byte[] bytes, int start, List<ushort> codes)
        {
            for (int index = start; index + 1 < bytes.Length; index += 2)
            {
                ushort raw = (ushort)((bytes[index] << 8) | bytes[index + 1]);
                if (raw != 0)
                {
                    codes.Add(raw);
                }
            }
        }
    }
}

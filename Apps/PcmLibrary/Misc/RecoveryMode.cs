// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// Shared rules for the explicit "PCM Recovery" operations, used by every UI so recovery behaves
    /// the same everywhere.
    /// </summary>
    /// <remarks>
    /// A PCM in recovery mode (its boot/reset pin grounded at power-up) sits in its resident boot
    /// loader and answers no operating-system query, so nothing can be auto-detected. The user tells
    /// us which PCM it is and we go straight in: no OSID query, no kernel probe, no bus detection.
    /// That is exactly what the forced-PCM-type path in <see cref="ReadManager"/> and
    /// <see cref="WriteManager"/> already does, so recovery simply forces the selected type.
    /// <para>
    /// We deliberately do NOT probe for recovery mode first. Detection is unreliable - it has only
    /// ever been confirmed on ObdLink ScanTool hardware, and other interfaces never see the reply -
    /// so a negative result would block the very rescue the user came here for.
    /// </para>
    /// </remarks>
    public static class RecoveryMode
    {
        /// <summary>
        /// Whether a recovery operation can be attempted for this PCM type. When false,
        /// <paramref name="reason"/> is a user-facing explanation.
        /// </summary>
        public static bool CanAttempt(PcmType type, out string reason)
        {
            if (type == PcmType.Undefined)
            {
                reason = "Select the PCM type to recover. Recovery cannot auto-detect it, because a PCM "
                    + "in recovery mode does not report its operating system.";
                return false;
            }

            OSIDInfo info = new OSIDInfo(type);

            // Recovery applies to CAN PCMs too - GMW3110 defines the programmed state and the
            // programming flow for both buses - but only the VPW side is built. Say that plainly
            // rather than starting a VPW flow on a CAN PCM, which would simply time out.
            if (info.BusProtocol == BusProtocol.Can500k)
            {
                reason = $"Recovery for {type} (CAN bus) is not built yet. Only the VPW flow is implemented so far.";
                return false;
            }

            if (!info.IsSupported)
            {
                reason = $"The {type} PCM is not supported.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Describe what a PCM broadcasting the programming request ($A2) says about itself.
        /// </summary>
        /// <remarks>
        /// The state byte follows GMW3110's programmedState levels, except at 0x00. This generation of
        /// PCM sends 0x00 to mean programming is needed, where the 2010 standard assigns it to "fully
        /// programmed" - and a fully programmed PCM would boot and run rather than sit here asking.
        /// So 0x00 is reported as a request without a level, and only the levels that mean the same
        /// thing in both generations are named.
        /// </remarks>
        public static string DescribeProgrammedState(byte state)
        {
            switch (state)
            {
                case 0x00:
                    return "programming is needed (this generation does not say which parts are missing)";

                case 0x01:
                    return "it has no operating system and no calibration";

                case 0x02:
                    return "it has an operating system but no calibration";

                default:
                    return $"it reports programmed state 0x{state:X2}";
            }
        }

        /// <summary>
        /// The log line reporting whether the PCM is asking to be programmed. Advisory in both
        /// directions: several interfaces cannot see the broadcast, so silence proves nothing.
        /// </summary>
        public static string DescribeProgrammingRequest(ProgrammingRequest? request)
        {
            return request != null
                ? $"The PCM is asking to be programmed on {request.Bus}: {DescribeProgrammedState(request.State)}."
                : "No programming request seen. Continuing anyway - not every interface can detect one.";
        }

        /// <summary>The log line announcing that a recovery operation is starting.</summary>
        public static string DescribeEntry(PcmType type, bool isWrite)
        {
            return $"PCM Recovery: entering recovery {(isWrite ? "write" : "read")} for a {type} PCM. "
                + "Detection is skipped - the selected PCM type is used as-is.";
        }
    }
}

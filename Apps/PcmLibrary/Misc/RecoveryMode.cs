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

            // CAN recovery is not implemented yet. Say so plainly rather than starting a VPW flow on a
            // CAN PCM, which would simply time out.
            if (info.BusProtocol == BusProtocol.Can500k)
            {
                reason = $"Recovery is not yet supported for {type} (CAN bus). Only VPW PCMs can be recovered.";
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

        /// <summary>The log line announcing that a recovery operation is starting.</summary>
        public static string DescribeEntry(PcmType type, bool isWrite)
        {
            return $"PCM Recovery: entering recovery {(isWrite ? "write" : "read")} for a {type} PCM. "
                + "Detection is skipped - the selected PCM type is used as-is.";
        }
    }
}

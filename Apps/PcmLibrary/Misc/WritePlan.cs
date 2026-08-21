// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// The single decision about what a write operation will actually touch, shared by the VPW
    /// (<see cref="CKernelWriter"/>) and CAN (<see cref="CanKernelWriter"/>) writers.
    /// </summary>
    /// <remarks>
    /// Both writers ask two questions: "will this range be erased/written?" and "does that plan
    /// require a boot-sector write this PCM cannot do?". Those answers MUST come from the same
    /// place. When they did not, a forced full write could pass the boot-sector gate (which
    /// evaluated the plan by CRC) and then write boot anyway (because the write loop honoured the
    /// force flag) - turning a recoverable PCM into a hard brick on a P05/P05b/P12/P05c.
    /// </remarks>
    public static class WritePlan
    {
        /// <summary>
        /// Whether this range will be erased and written by the operation.
        /// </summary>
        /// <param name="forceAllSectors">
        /// True for a forced full-write pass, which rewrites every in-scope sector even when its
        /// on-device CRC already matches the image. Scope (image size and relevant block types) is
        /// still honoured: forcing rewrites more sectors, it never widens what is in scope.
        /// </param>
        public static bool ShouldProcessRange(
            MemoryRange range,
            BlockType relevantBlocks,
            WriteType writeType,
            UInt32 effectiveImageSize,
            bool forceAllSectors)
        {
            // Out of scope for this image. The P10 has the same flash chip as the P59, but the high
            // bit of the address bus isn't connected, so talking to the top 512kb is a hardware
            // error; for P10/P11 the usable size is the smaller PCM-type size.
            if (range.Address >= effectiveImageSize)
            {
                return false;
            }

            // Not a block type this operation covers (e.g. boot is not in scope for a calibration
            // write). Never processed, forced or not.
            if ((range.Type & relevantBlocks) == 0)
            {
                return false;
            }

            // A forced pass includes every in-scope sector regardless of CRC.
            if (forceAllSectors)
            {
                return true;
            }

            // Already identical on the device, so there is nothing to do (a test write always runs,
            // because the point of it is to exercise the transfer).
            if ((range.ActualCrc == range.DesiredCrc) && (writeType != WriteType.TestWrite))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the PCM's boot-sector policy permits this write plan. False means the operation
        /// must abort before anything is erased.
        /// </summary>
        /// <remarks>
        /// A PCM that cannot write its boot sector may still be cloned, as long as the plan does not
        /// actually write boot - i.e. the image's boot sector already matches the PCM's, or boot is
        /// not in scope. Only a plan that would erase/write a boot range is refused. Pass the same
        /// <paramref name="forceAllSectors"/> the write loop uses, or the gate and the loop can
        /// disagree about whether boot is included.
        /// </remarks>
        public static bool BootPolicyAllowsWritePlan(
            WriteType writeType,
            bool supportsBootSectorWrite,
            BlockType relevantBlocks,
            UInt32 effectiveImageSize,
            IEnumerable<MemoryRange> memoryRanges,
            bool forceAllSectors)
        {
            // Compare and test-write are non-destructive.
            if (writeType == WriteType.Compare || writeType == WriteType.TestWrite)
            {
                return true;
            }

            // Boot sector writes are supported; allow all write plans.
            if (supportsBootSectorWrite)
            {
                return true;
            }

            foreach (MemoryRange range in memoryRanges)
            {
                if (!ShouldProcessRange(range, relevantBlocks, writeType, effectiveImageSize, forceAllSectors))
                {
                    continue;
                }

                // This plan would erase/write boot on a PCM that cannot rewrite it.
                if ((range.Type & BlockType.Boot) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

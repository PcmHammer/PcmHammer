// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the CAN writer's boot-sector write policy (CanKernelWriter.BootPolicyAllowsWritePlan).
    /// This is the gate that, on a PCM whose IsSupportedWriteBootSector is false (e.g. P05c), aborts a
    /// destructive write before any erase when the plan would rewrite a boot range that differs from the
    /// image. It mirrors the long-standing VPW protection so the CAN path cannot clobber the boot sector.
    /// </summary>
    [TestClass]
    public class CanKernelBootPolicyTests
    {
        private const uint ImageSize = 1024 * 1024;

        // A 1 MiB-style layout with the boot sector at address 0, like the real flash chips.
        private static List<MemoryRange> Layout(uint bootActualCrc, uint bootDesiredCrc, uint calActualCrc, uint calDesiredCrc)
        {
            return new List<MemoryRange>
            {
                new MemoryRange(0x10000, 0x10000, BlockType.OperatingSystem) { ActualCrc = 1, DesiredCrc = 1 },
                new MemoryRange(0x08000, 0x08000, BlockType.Calibration) { ActualCrc = calActualCrc, DesiredCrc = calDesiredCrc },
                new MemoryRange(0x06000, 0x02000, BlockType.Parameter) { ActualCrc = 1, DesiredCrc = 1 },
                new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = bootActualCrc, DesiredCrc = bootDesiredCrc },
            };
        }

        // ---- PCM that cannot write its boot sector (P05c, P05, P05b) ----

        [TestMethod]
        public void NoBootSupport_FullWrite_BootDiffers_IsBlocked()
        {
            // Boot CRC mismatch -> the plan would erase/write boot -> abort.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 1, calDesiredCrc: 1);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges);

            Assert.IsFalse(allowed);
        }

        [TestMethod]
        public void NoBootSupport_FullWrite_BootMatches_IsAllowed()
        {
            // Boot CRC matches the image, so ShouldProcess skips the boot range and only the differing
            // calibration is written. A normal reflash of the same hardware proceeds.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xAAAA, calActualCrc: 0x1111, calDesiredCrc: 0x2222);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges);

            Assert.IsTrue(allowed);
        }

        [TestMethod]
        public void NoBootSupport_CalibrationWrite_BootDiffers_IsAllowed()
        {
            // Even though boot differs, a Calibration write does not include boot in relevantBlocks,
            // so the boot range is never in the plan.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 0x1111, calDesiredCrc: 0x2222);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Calibration, supportsBootSectorWrite: false, BlockType.Calibration, ImageSize, ranges);

            Assert.IsTrue(allowed);
        }

        [TestMethod]
        public void NoBootSupport_Compare_IsAllowed_EvenWhenBootDiffers()
        {
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 1, calDesiredCrc: 1);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Compare, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges);

            Assert.IsTrue(allowed);
        }

        [TestMethod]
        public void NoBootSupport_TestWrite_IsAllowed_EvenWhenBootDiffers()
        {
            // Test write never erases or programs, so it is safe regardless of the boot CRC.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 1, calDesiredCrc: 1);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.TestWrite, supportsBootSectorWrite: false, BlockType.Calibration, ImageSize, ranges);

            Assert.IsTrue(allowed);
        }

        // ---- PCM that can write its boot sector ----

        [TestMethod]
        public void BootSupport_FullWrite_BootDiffers_IsAllowed()
        {
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 1, calDesiredCrc: 1);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: true, BlockType.All, ImageSize, ranges);

            Assert.IsTrue(allowed);
        }

        // ---- ShouldProcessRange ----

        [TestMethod]
        public void ShouldProcessRange_SkipsMatchingCrc_OnRealWrite()
        {
            var range = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 7, DesiredCrc = 7 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                range, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: false, supportsBootSectorWrite: true));
        }

        [TestMethod]
        public void ShouldProcessRange_ProcessesMatchingCrc_OnTestWrite()
        {
            // A test write exercises every relevant range regardless of CRC.
            var range = new MemoryRange(0x08000, 0x08000, BlockType.Calibration) { ActualCrc = 7, DesiredCrc = 7 };
            Assert.IsTrue(CanKernelWriter.ShouldProcessRange(
                range, BlockType.Calibration, WriteType.TestWrite, ImageSize,
                forceAllSectors: false, supportsBootSectorWrite: true));
        }

        [TestMethod]
        public void ShouldProcessRange_SkipsRangeOutsideImage()
        {
            var range = new MemoryRange(ImageSize, 0x04000, BlockType.OperatingSystem) { ActualCrc = 1, DesiredCrc = 2 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                range, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: false, supportsBootSectorWrite: true));
        }

        [TestMethod]
        public void ShouldProcessRange_SkipsIrrelevantBlockType()
        {
            var range = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 1, DesiredCrc = 2 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                range, BlockType.Calibration, WriteType.Full, ImageSize,
                forceAllSectors: false, supportsBootSectorWrite: true));
        }

        // ---- Forced full write (RuntimeSettings.ForceWriteAllSectors) ----
        // The gate must be evaluated with the same force flag the write loop uses. Otherwise a forced
        // write passes the gate by CRC and then erases boot anyway, turning a recoverable PCM into a
        // hard brick on hardware whose boot sector cannot be rewritten.

        [TestMethod]
        public void NoBootSupport_ForcedFullWrite_BootMatches_IsAllowed()
        {
            // Boot already matches, so there is nothing to gain by erasing and rewriting it - and on a
            // PCM that cannot rewrite boot that erase is the brick. Boot drops out of the plan and the
            // forced write proceeds with every other sector.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xAAAA, calActualCrc: 0x1111, calDesiredCrc: 0x2222);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges,
                forceAllSectors: true);

            Assert.IsTrue(allowed);
            Assert.IsTrue(WritePlan.ForcedPlanExcludesBoot(
                WriteType.Full, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges,
                forceAllSectors: true));
        }

        [TestMethod]
        public void NoBootSupport_ForcedFullWrite_BootDiffers_IsBlocked()
        {
            // Boot genuinely needs writing and this PCM cannot do it. Refuse before anything is erased.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 0x1111, calDesiredCrc: 0x2222);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: false, BlockType.All, ImageSize, ranges,
                forceAllSectors: true);

            Assert.IsFalse(allowed);
        }

        [TestMethod]
        public void ForcedAbortMessage_NamesForceAsTheCause()
        {
            string[] forced = WritePlan.DescribeBootSectorAbort(PcmType.P05b, forceAllSectors: true);
            StringAssert.Contains(forced[0], "Force write all sectors");
            StringAssert.Contains(forced[1], "Abort:");

            string[] plain = WritePlan.DescribeBootSectorAbort(PcmType.P05b, forceAllSectors: false);
            Assert.IsFalse(plain[0].Contains("Force write all sectors"));
        }

        [TestMethod]
        public void NoBootSupport_ForcedCalibrationWrite_IsAllowed()
        {
            // Forcing rewrites more sectors, but it never widens scope: boot is not in relevantBlocks
            // for a calibration write, so the clone is still allowed on a no-boot-write PCM.
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xBBBB, calActualCrc: 0x1111, calDesiredCrc: 0x2222);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Calibration, supportsBootSectorWrite: false, BlockType.Calibration, ImageSize, ranges,
                forceAllSectors: true);

            Assert.IsTrue(allowed);
        }

        [TestMethod]
        public void BootSupport_ForcedFullWrite_IsAllowed()
        {
            var ranges = Layout(bootActualCrc: 0xAAAA, bootDesiredCrc: 0xAAAA, calActualCrc: 1, calDesiredCrc: 1);

            bool allowed = CanKernelWriter.BootPolicyAllowsWritePlan(
                WriteType.Full, supportsBootSectorWrite: true, BlockType.All, ImageSize, ranges,
                forceAllSectors: true);

            Assert.IsTrue(allowed);
        }

        [TestMethod]
        public void ShouldProcessRange_Forced_ProcessesMatchingCrc()
        {
            // A PCM that CAN rewrite boot honours the force flag everywhere, boot included.
            var range = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 7, DesiredCrc = 7 };
            Assert.IsTrue(CanKernelWriter.ShouldProcessRange(
                range, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: true));
        }

        [TestMethod]
        public void ShouldProcessRange_Forced_SkipsMatchingBoot_WhenBootIsUnwritable()
        {
            // Same forced write on a PCM that cannot rewrite boot: the identical boot range is left
            // out rather than erased and rewritten as a no-op.
            var range = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 7, DesiredCrc = 7 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                range, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: false));
        }

        [TestMethod]
        public void ShouldProcessRange_Forced_KeepsDifferingBoot_WhenBootIsUnwritable()
        {
            // Differing boot stays in the plan so the boot-policy gate can refuse the whole operation.
            var range = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 7, DesiredCrc = 8 };
            Assert.IsTrue(CanKernelWriter.ShouldProcessRange(
                range, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: false));
        }

        [TestMethod]
        public void ShouldProcessRange_Forced_NonBootIsStillForced_WhenBootIsUnwritable()
        {
            // The boot carve-out must not weaken forcing anywhere else.
            var cal = new MemoryRange(0x08000, 0x08000, BlockType.Calibration) { ActualCrc = 7, DesiredCrc = 7 };
            Assert.IsTrue(CanKernelWriter.ShouldProcessRange(
                cal, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: false));
        }

        [TestMethod]
        public void ShouldProcessRange_Forced_StillSkipsOutOfScopeRanges()
        {
            // Force must not widen scope: out-of-image and irrelevant-block ranges stay skipped.
            var outside = new MemoryRange(ImageSize, 0x04000, BlockType.OperatingSystem) { ActualCrc = 1, DesiredCrc = 2 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                outside, BlockType.All, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: true));

            var irrelevant = new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 1, DesiredCrc = 2 };
            Assert.IsFalse(CanKernelWriter.ShouldProcessRange(
                irrelevant, BlockType.Calibration, WriteType.Full, ImageSize,
                forceAllSectors: true, supportsBootSectorWrite: true));
        }
    }
}

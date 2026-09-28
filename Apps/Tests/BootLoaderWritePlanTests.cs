// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The boot-loader write plan: given a CRC comparison and a slave-id decision, which block groups
    /// get programmed. Drives what a boot-loader PCM writes, so only changed groups are touched.
    /// </summary>
    [TestClass]
    public class BootLoaderWritePlanTests
    {
        private const BlockType AllMaster = BlockType.OperatingSystem | BlockType.Calibration;

        // Two calibration ranges and one OS range, with the CRCs the caller would have filled in.
        private static List<MemoryRange> Ranges(bool calDiffers, bool osDiffers)
        {
            return new List<MemoryRange>
            {
                Range(0x040000, 0x20000, BlockType.Calibration, calDiffers),
                Range(0x080000, 0x40000, BlockType.Calibration, false),
                Range(0x0C0000, 0x40000, BlockType.OperatingSystem, osDiffers),
            };
        }

        private static MemoryRange Range(uint address, uint size, BlockType type, bool differs)
        {
            var range = new MemoryRange(address, size, type);
            range.DesiredCrc = 0x11111111u;
            range.ActualCrc = differs ? 0x22222222u : 0x11111111u;
            return range;
        }

        [TestMethod]
        public void OnlyTheGroupsThatDifferAreWritten()
        {
            BootLoaderWritePlan calOnly = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: true, osDiffers: false), AllMaster, forceAll: false, slaveInScope: false, slaveDiffers: false);
            Assert.AreEqual(BlockType.Calibration, calOnly.MasterBlocks);
            Assert.IsFalse(calOnly.WriteSlave);

            BootLoaderWritePlan osOnly = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: false, osDiffers: true), AllMaster, forceAll: false, slaveInScope: false, slaveDiffers: false);
            Assert.AreEqual(BlockType.OperatingSystem, osOnly.MasterBlocks);

            BootLoaderWritePlan both = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: true, osDiffers: true), AllMaster, forceAll: false, slaveInScope: false, slaveDiffers: false);
            Assert.AreEqual(AllMaster, both.MasterBlocks);
        }

        [TestMethod]
        public void NothingToWriteWhenEverythingMatches()
        {
            BootLoaderWritePlan plan = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: false, osDiffers: false), AllMaster, forceAll: false, slaveInScope: false, slaveDiffers: false);

            Assert.IsTrue(plan.NothingToWrite);
            Assert.AreEqual("nothing (already up to date)", plan.Describe());
        }

        [TestMethod]
        public void RequestedScopeLimitsThePlan()
        {
            // OS differs, but a calibration write must never pull the OS into the plan.
            BootLoaderWritePlan plan = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: false, osDiffers: true), BlockType.Calibration, forceAll: false, slaveInScope: false, slaveDiffers: false);

            Assert.IsTrue(plan.NothingToWrite);
        }

        [TestMethod]
        public void ForceWritesEveryRequestedGroupRegardlessOfCrc()
        {
            BootLoaderWritePlan plan = BootLoaderWritePlan.FromComparison(
                Ranges(calDiffers: false, osDiffers: false), AllMaster, forceAll: true, slaveInScope: true, slaveDiffers: false);

            Assert.AreEqual(AllMaster, plan.MasterBlocks);
            Assert.IsTrue(plan.WriteSlave);
        }

        [TestMethod]
        public void Forced_LimitsToTheRequestedBlocks()
        {
            // Force + calibration writes only the calibration sectors, never the OS or boot.
            BootLoaderWritePlan cal = BootLoaderWritePlan.Forced(BlockType.Calibration, slaveInScope: false);
            Assert.AreEqual(BlockType.Calibration, cal.MasterBlocks);
            Assert.IsFalse(cal.WriteSlave);

            // Force + full covers the OS and calibration, and the slave when it is in scope.
            BootLoaderWritePlan full = BootLoaderWritePlan.Forced(AllMaster, slaveInScope: true);
            Assert.AreEqual(AllMaster, full.MasterBlocks);
            Assert.IsTrue(full.WriteSlave);
        }

        [TestMethod]
        public void SlaveIsWrittenOnlyWhenInScopeAndDifferent()
        {
            var same = Ranges(calDiffers: false, osDiffers: false);

            Assert.IsFalse(BootLoaderWritePlan.FromComparison(same, AllMaster, false, slaveInScope: true, slaveDiffers: false).WriteSlave);
            Assert.IsTrue(BootLoaderWritePlan.FromComparison(same, AllMaster, false, slaveInScope: true, slaveDiffers: true).WriteSlave);
            Assert.IsFalse(BootLoaderWritePlan.FromComparison(same, AllMaster, false, slaveInScope: false, slaveDiffers: true).WriteSlave);
        }

        [TestMethod]
        public void SlaveModulePartNumber_ReadsPlainAndWrappedModules()
        {
            // Plain module: eight ASCII digits at offset 0x10.
            byte[] plain = new byte[0x20];
            System.Text.Encoding.ASCII.GetBytes("12667480").CopyTo(plain, 0x10);
            Assert.IsTrue(FlashModuleBuilder.TryGetSlaveModulePartNumber(plain, out uint plainId));
            Assert.AreEqual(12667480u, plainId);

            // Wrapped module: the same header after decode.
            byte[] wrapped = E92ModuleCodec.Wrap(E92ModuleCodec.Compress(plain));
            Assert.IsTrue(FlashModuleBuilder.TryGetSlaveModulePartNumber(wrapped, out uint wrappedId));
            Assert.AreEqual(12667480u, wrappedId);
        }

        [TestMethod]
        public void SlaveModulePartNumber_RejectsNonNumericHeader()
        {
            byte[] notaPartNumber = new byte[0x20];
            for (int i = 0; i < notaPartNumber.Length; i++)
            {
                notaPartNumber[i] = 0xFF;
            }

            Assert.IsFalse(FlashModuleBuilder.TryGetSlaveModulePartNumber(notaPartNumber, out _));
            Assert.IsFalse(FlashModuleBuilder.TryGetSlaveModulePartNumber(new byte[4], out _));
        }
    }
}

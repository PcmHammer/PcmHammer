// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Pins the AMD 29BDD160G flash layout. Each MemoryRange is exactly one hardware erase sector,
    /// so getting a boundary wrong erases the wrong flash. The dual-boot 29BDD160G is 8 KiB x8 at the
    /// bottom, 64 KiB x30 in the middle, and 8 KiB x8 at the top.
    /// </summary>
    [TestClass]
    public class FlashChipTests
    {
        private const uint Amd29BDD160GChipId = 0x0001007E;

        private static FlashChip Amd29BDD160G() => FlashChip.Create(Amd29BDD160GChipId, new MockLogger());

        // MemoryRanges sorted by ascending address, which is the order the sectors sit in flash.
        private static List<MemoryRange> Amd29BDD160GRangesByAddress() =>
            Amd29BDD160G().MemoryRanges.OrderBy(r => r.Address).ToList();

        [TestMethod]
        public void Amd29BDD160G_HasOneRangePerHardwareSector()
        {
            List<MemoryRange> ranges = Amd29BDD160GRangesByAddress();

            Assert.AreEqual(8 + 30 + 8, ranges.Count, "Eight 8 KiB, thirty 64 KiB, eight 8 KiB.");
            Assert.AreEqual(0x000000u, ranges[0].Address);
            Assert.AreEqual(0x010000u, ranges[8].Address, "First 64 KiB sector follows eight 8 KiB sectors.");
            Assert.AreEqual(0x1F0000u, ranges[38].Address, "Top 8 KiB sectors follow thirty 64 KiB sectors.");
            Assert.AreEqual(0x1FE000u, ranges[45].Address, "Last sector starts one 8 KiB sector below the top.");
        }

        [TestMethod]
        public void Amd29BDD160G_SectorSizes_Are8KiBAtEndsAnd64KiBInTheMiddle()
        {
            List<uint> sizes = Amd29BDD160GRangesByAddress().Select(r => r.Size).ToList();

            Assert.IsTrue(sizes.Take(8).All(s => s == 0x2000), "Bottom region is 8 KiB sectors.");
            Assert.IsTrue(sizes.Skip(8).Take(30).All(s => s == 0x10000), "Middle region is 64 KiB sectors.");
            Assert.IsTrue(sizes.Skip(38).All(s => s == 0x2000), "Top region is 8 KiB sectors.");
        }

        [TestMethod]
        public void Amd29BDD160G_Ranges_CoverTheWholeChipContiguously()
        {
            FlashChip chip = Amd29BDD160G();
            List<MemoryRange> ranges = Amd29BDD160GRangesByAddress();

            uint expected = 0;
            foreach (MemoryRange range in ranges)
            {
                Assert.AreEqual(expected, range.Address,
                    $"Gap or overlap before 0x{range.Address:X6}.");
                expected += range.Size;
            }

            Assert.AreEqual(chip.Size, expected, "Ranges must cover exactly the whole chip.");
        }

        [TestMethod]
        public void Amd29BDD160G_BlockTypes_GroupSectorsIntoLogicalRegions()
        {
            List<MemoryRange> ranges = Amd29BDD160GRangesByAddress();

            Assert.AreEqual(BlockType.Boot, ranges[0].Type, "Bottom sector is boot.");
            Assert.IsTrue(ranges.Skip(1).Take(5).All(r => r.Type == BlockType.OperatingSystem),
                "Five 8 KiB operating system sectors follow boot.");
            Assert.IsTrue(ranges.Skip(6).Take(2).All(r => r.Type == BlockType.Parameter),
                "Two 8 KiB parameter sectors.");
            Assert.IsTrue(ranges.Skip(8).Take(27).All(r => r.Type == BlockType.OperatingSystem),
                "Twenty-seven 64 KiB operating system sectors.");
            Assert.IsTrue(ranges.Skip(35).All(r => r.Type == BlockType.Calibration),
                "Top region (three 64 KiB plus eight 8 KiB sectors) is calibration.");
        }
    }

    /// <summary>
    /// Pins the Freescale MPC5674F on-chip C90LC flash layout used by the GM E92. Each MemoryRange is
    /// one hardware erase sector, so a wrong boundary erases the wrong flash. The 4 MiB array is a
    /// 256 KiB low address space (16K x4, 64K x2, 16K x4), a 256 KiB mid address space (128K x2), and
    /// a 3.5 MiB high address space (256K x14). Sizes are byte counts (embedded flash is byte-addressed,
    /// unlike the x16 Intel/AMD parts). The whole low address space is the protected boot region.
    /// </summary>
    [TestClass]
    public class Mpc5674fFlashChipTests
    {
        private const uint Mpc5674fChipId = 0x56746020;

        private static FlashChip Mpc5674f() => FlashChip.Create(Mpc5674fChipId, new MockLogger());

        // MemoryRanges sorted by ascending address, which is the order the sectors sit in flash.
        private static List<MemoryRange> RangesByAddress() =>
            Mpc5674f().MemoryRanges.OrderBy(r => r.Address).ToList();

        [TestMethod]
        public void Mpc5674f_IsFourMebibytes()
        {
            Assert.AreEqual(4096u * 1024u, Mpc5674f().Size);
        }

        [TestMethod]
        public void Mpc5674f_HasOneRangePerHardwareSector()
        {
            // Low: 16K x4 + 64K x2 + 16K x4 = 10 sectors. Mid: 128K x2 = 2. High: 256K x14 = 14.
            Assert.AreEqual(10 + 2 + 14, RangesByAddress().Count);
        }

        [TestMethod]
        public void Mpc5674f_LowAddressSpace_Is16KiBAndsAround64KiB()
        {
            List<uint> sizes = RangesByAddress().Select(r => r.Size).ToList();

            Assert.IsTrue(sizes.Take(4).All(s => s == 0x4000), "Four 16 KiB sectors at the bottom.");
            Assert.IsTrue(sizes.Skip(4).Take(2).All(s => s == 0x10000), "Two 64 KiB sectors.");
            Assert.IsTrue(sizes.Skip(6).Take(4).All(s => s == 0x4000), "Four 16 KiB sectors close the low space.");
        }

        [TestMethod]
        public void Mpc5674f_MidAndHighAddressSpaces_Are128KiBThen256KiB()
        {
            List<uint> sizes = RangesByAddress().Select(r => r.Size).ToList();

            Assert.IsTrue(sizes.Skip(10).Take(2).All(s => s == 0x20000), "Mid space is two 128 KiB sectors.");
            Assert.IsTrue(sizes.Skip(12).All(s => s == 0x40000), "High space is 256 KiB sectors.");
        }

        [TestMethod]
        public void Mpc5674f_AddressSpaceBoundaries_AreWhereExpected()
        {
            List<MemoryRange> ranges = RangesByAddress();

            Assert.AreEqual(0x000000u, ranges[0].Address, "Low address space starts at zero.");
            Assert.AreEqual(0x040000u, ranges[10].Address, "Mid address space follows the 256 KiB low space.");
            Assert.AreEqual(0x080000u, ranges[12].Address, "High address space follows the 256 KiB mid space.");
            Assert.AreEqual(0x3C0000u, ranges[25].Address, "Last sector is the top 256 KiB block.");
        }

        [TestMethod]
        public void Mpc5674f_Ranges_CoverTheWholeChipContiguously()
        {
            FlashChip chip = Mpc5674f();

            uint expected = 0;
            foreach (MemoryRange range in RangesByAddress())
            {
                Assert.AreEqual(expected, range.Address, $"Gap or overlap before 0x{range.Address:X6}.");
                expected += range.Size;
            }

            Assert.AreEqual(chip.Size, expected, "Ranges must cover exactly the whole chip.");
        }

        [TestMethod]
        public void Mpc5674f_BlockTypes_MatchTheUniversalPatcherSegmentMap()
        {
            List<MemoryRange> ranges = RangesByAddress();

            // Low address space (ten sectors) is the protected boot region.
            Assert.IsTrue(ranges.Take(10).All(r => r.Type == BlockType.Boot),
                "The whole 256 KiB low address space is the protected boot region.");

            // Calibration is 0x40000..0xC0000: both 128 KiB mid sectors plus the first 256 KiB high sector.
            Assert.IsTrue(ranges.Skip(10).Take(3).All(r => r.Type == BlockType.Calibration),
                "Calibration is the two 128 KiB mid sectors and the first 256 KiB high sector.");
            Assert.AreEqual(0x040000u, ranges[10].Address);
            Assert.AreEqual(0x0C0000u, ranges[13].Address, "Calibration ends and OS begins at 0xC0000.");

            // Operating system is 0xC0000..0x400000: the remaining thirteen 256 KiB high sectors.
            Assert.IsTrue(ranges.Skip(13).All(r => r.Type == BlockType.OperatingSystem),
                "The top thirteen 256 KiB sectors are the operating system.");
        }
    }
}

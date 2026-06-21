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
}

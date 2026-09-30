// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// The E39/E39a read+write profile, its OSID mapping, and the kernel upload plan it produces.
    /// </summary>
    [TestClass]
    public class E39Tests
    {
        [TestMethod]
        public void KnownOsids_MapToE39a()
        {
            var bench = new OSIDInfo(12642819u);
            Assert.AreEqual(PcmType.E39a, bench.HardwareType);
            Assert.AreEqual(12642665L, (long)bench.ServiceNumber);

            var sample = new OSIDInfo(12664221u);
            Assert.AreEqual(PcmType.E39a, sample.HardwareType);
            Assert.AreEqual(12653998L, (long)sample.ServiceNumber);
        }

        [TestMethod]
        public void E39AndE39a_ShareOneWriteCapableProfile()
        {
            foreach (PcmType type in new[] { PcmType.E39, PcmType.E39a })
            {
                var info = new OSIDInfo(type);
                Assert.AreEqual(type, info.HardwareType);
                Assert.IsTrue(info.IsSupportedRead, type + " read");
                // Native C90FL erase/program is hardware-validated on E39a; E39 shares this profile.
                Assert.IsTrue(info.IsSupportedWrite, type + " write");
                Assert.IsTrue(info.IsSupportedBootLoaderWrite, type + " boot loader write");
                Assert.IsFalse(info.RequiresBootLoaderWrite, type + " requires boot loader write");
                Assert.IsTrue(info.IsSupportedWriteBySegment, type + " write by segment");
                Assert.IsTrue(info.IsSupportedWriteSlaveCPU, type + " write slave CPU");
                // Boot has invalid ECC and the kernel refuses it, so it stays out of any write.
                Assert.IsFalse(info.IsSupportedWriteBootSector, type + " write boot sector");
                Assert.IsFalse(info.HasParameterBlocks, type + " parameter blocks");
                Assert.IsTrue(info.ChecksumSupport, type + " checksum support");
                Assert.AreEqual(BusProtocol.Can500k, info.BusProtocol);
                Assert.AreEqual(GMLANProtocol.E38, info.GMLANProtocol);
                Assert.AreEqual("Kernel-E39.bin", info.KernelFileName);
                Assert.AreEqual(0x40007000, info.KernelBaseAddress);
                Assert.AreEqual(0x40007004, info.KernelRunAddress);
                Assert.AreEqual(0x300000, info.ImageSize);
                Assert.AreEqual(0x800, info.KernelReadBlockSize);
                Assert.AreEqual(0xDC, info.KeyAlgorithm);
            }
        }

        /// <summary>
        /// The 2092-byte E39 kernel is over one 2048-byte block, so it goes up as the E38 dialect's
        /// tail copy block (past the +4 seam) followed by the executing block.
        /// </summary>
        [TestMethod]
        public void Upload_2092ByteKernel_IsTailCopyThenExecutingBlock()
        {
            var info = new OSIDInfo(PcmType.E39a);
            byte[] payload = Enumerable.Range(0, 2092).Select(i => (byte)i).ToArray();

            CanKernelUpload upload = CanKernelUploadProtocol.For(info.GMLANProtocol).BuildUpload(
                new Gmlan(), payload, (uint)info.KernelBaseAddress, (uint)info.KernelRunAddress, maxBlockSize: 2048);

            Assert.AreEqual(2, upload.Blocks.Count);

            // Tail: 2092 - 2038 = 54 bytes at load + 2038 + 4.
            byte[] tail = upload.Blocks[0].Message.GetBytes();
            Assert.IsFalse(upload.Blocks[0].IsExecuting);
            CollectionAssert.AreEqual(new byte[] { 0x36, 0x00, 0x40, 0x00, 0x77, 0xFA }, tail.Take(6).ToArray());
            CollectionAssert.AreEqual(payload.Skip(2038).ToArray(), tail.Skip(6).ToArray());

            byte[] exec = upload.Blocks[1].Message.GetBytes();
            Assert.IsTrue(upload.Blocks[1].IsExecuting);
            CollectionAssert.AreEqual(
                new byte[] { 0x36, 0x80, 0x40, 0x00, 0x70, 0x00, 0x40, 0x00, 0x70, 0x04 }, exec.Take(10).ToArray());
            CollectionAssert.AreEqual(payload.Take(2038).ToArray(), exec.Skip(10).ToArray());

            // RequestDownload declares both framed blocks: 60 + 2048 = 2108 = 0x83C.
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x08, 0x3C }, upload.RequestDownload.GetBytes());
        }

        /// <summary>
        /// The master flash segments a boot-loader write streams, in order:
        /// the OS first (leading with the 0x800 checksum table at 0xCB000), then the five calibration
        /// segments tiling 0x020000..0x080000 contiguously. Together they cover every writable byte above
        /// the protected boot block.
        /// </summary>
        [TestMethod]
        public void MasterSegments_MatchTheImageLayout()
        {
            byte[] image = LoadE39aTestImage();
            List<FlashSegment> segments = FileValidator.GetE39MasterSegments(image);

            Assert.AreEqual(6, segments.Count);

            // OS first, then five calibration segments.
            Assert.AreEqual(BlockType.OperatingSystem, segments[0].BlockType);
            Assert.IsTrue(segments.Skip(1).All(s => s.BlockType == BlockType.Calibration));

            // OS spans the high flash and leads with the 0x800 master checksum table at 0xCB000.
            Assert.AreEqual(0x080000, segments[0].Start);
            Assert.AreEqual(0x2FFFFF, segments[0].End);
            Assert.AreEqual(0xCB000 - 0x080000, segments[0].HeaderOffset);

            // The five calibration segments tile 0x020000..0x080000 contiguously.
            List<FlashSegment> cals = segments.Skip(1).OrderBy(s => s.Start).ToList();
            Assert.AreEqual(0x020000, cals[0].Start);
            Assert.AreEqual(0x07FFFF, cals[cals.Count - 1].End);
            for (int i = 1; i < cals.Count; i++)
            {
                Assert.AreEqual(cals[i - 1].End + 1, cals[i].Start, "Calibration segments must tile contiguously.");
            }
        }

        /// <summary>
        /// A representative OSID from each imported service-number block resolves to its type and
        /// service number. E39 and E78 fall through to the E39a profile but keep their own identity.
        /// </summary>
        [TestMethod]
        public void ImportedOsids_MapToTypeAndServiceNumber()
        {
            AssertOsid(12648907, PcmType.E39, 12651994);
            AssertOsid(12644447, PcmType.E39a, 12653998);
            AssertOsid(12640467, PcmType.E78, 12642100);
            AssertOsid(12646746, PcmType.E78, 12643636);
            AssertOsid(12687343, PcmType.E78, 12668986);
            AssertOsid(12656658, PcmType.E92, 12656993);
            AssertOsid(12670573, PcmType.E92, 12672537);
            AssertOsid(12674021, PcmType.E92, 12674052);
            AssertOsid(12690174, PcmType.E92, 12703872);
            AssertOsid(12683365, PcmType.E92, 12704475);
        }

        private static void AssertOsid(uint osid, PcmType expectedType, long expectedService)
        {
            var info = new OSIDInfo(osid);
            Assert.AreEqual(expectedType, info.HardwareType, "OSID " + osid + " hardware type");
            Assert.AreEqual(expectedService, (long)info.ServiceNumber, "OSID " + osid + " service number");
        }

        private static byte[] LoadE39aTestImage()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "TestData", "E39a_3072KiB_12664221.bin");
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }

            throw new InvalidOperationException("E39a test image not found above " + AppContext.BaseDirectory);
        }
    }
}

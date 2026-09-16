// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// The E39/E39a read profile, its OSID mapping, and the kernel upload plan it produces.
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

            var captiva = new OSIDInfo(12655477u);
            Assert.AreEqual(PcmType.E39a, captiva.HardwareType);
            Assert.AreEqual(0L, (long)captiva.ServiceNumber);
        }

        [TestMethod]
        public void E39AndE39a_ShareOneReadOnlyProfile()
        {
            foreach (PcmType type in new[] { PcmType.E39, PcmType.E39a })
            {
                var info = new OSIDInfo(type);
                Assert.AreEqual(type, info.HardwareType);
                Assert.IsTrue(info.IsSupportedRead, type + " read");
                Assert.IsFalse(info.IsSupportedWrite, type + " write");
                Assert.IsFalse(info.IsSupportedBootLoaderWrite, type + " boot loader write");
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
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the CAN kernel-upload protocol variants (Messages/CanKernelUploadProtocol.cs). They
    /// verify that each PCM generation builds its own upload plan - E38 (executing block carries
    /// [run-addr][code], +4 seam) and the oddball P05c (literal-address copies, bare execute frame,
    /// 16-bit RequestDownload) - so the two cannot drift into each other. Both kernels announce their
    /// launch with 0x99.
    /// </summary>
    [TestClass]
    public class CanKernelUploadProtocolTests
    {
        private static readonly Gmlan Gmlan = new Gmlan();

        // ---- Selection ----

        [TestMethod]
        public void For_SelectsPerPcmDialect()
        {
            Assert.IsInstanceOfType(CanKernelUploadProtocol.For(GMLANProtocol.E38), typeof(E38KernelUploadProtocol));
            Assert.IsInstanceOfType(CanKernelUploadProtocol.For(GMLANProtocol.P05c), typeof(P05cKernelUploadProtocol));
        }

        [TestMethod]
        [ExpectedException(typeof(System.InvalidOperationException))]
        public void For_None_Throws()
        {
            CanKernelUploadProtocol.For(GMLANProtocol.None);
        }

        // ---- E38 ----

        [TestMethod]
        public void E38_SingleBlock_IsExecutingWithRunAddrAndCode()
        {
            var protocol = new E38KernelUploadProtocol();
            byte[] payload = { 0xBE, 0xEF };

            CanKernelUpload upload = protocol.BuildUpload(Gmlan, payload, 0x003FC430, 0x003FC434, maxBlockSize: 4096);

            Assert.AreEqual(1, upload.Blocks.Count);
            Assert.IsTrue(upload.Blocks[0].IsExecuting);
            CollectionAssert.AreEqual(
                new byte[] { 0x36, 0x80, 0x00, 0x3F, 0xC4, 0x30, 0x00, 0x3F, 0xC4, 0x34, 0xBE, 0xEF },
                upload.Blocks[0].Message.GetBytes());

            // RequestDownload declares the framed total (12 bytes) with a 3-byte size field.
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x00, 0x0C }, upload.RequestDownload.GetBytes());
        }

        [TestMethod]
        public void E38_MultiBlock_CopiesTargetLoadPlusOffsetPlusFour_HighestFirst_ExecLast()
        {
            var protocol = new E38KernelUploadProtocol();
            // blockSize = maxBlockSize - 10 = 4. 10 bytes -> two full blocks + a 2-byte tail.
            byte[] payload = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();

            CanKernelUpload upload = protocol.BuildUpload(Gmlan, payload, 0x00FF8000, 0x00FF8000, maxBlockSize: 14);

            Assert.AreEqual(3, upload.Blocks.Count);

            // Highest offset (the 2-byte tail) first, copy at load + 8 + 4 = 0xFF800C.
            AssertCopyBlock(upload.Blocks[0], 0x00FF800C, new byte[] { 8, 9 });
            // Then the offset-4 full block, copy at load + 4 + 4 = 0xFF8008.
            AssertCopyBlock(upload.Blocks[1], 0x00FF8008, new byte[] { 4, 5, 6, 7 });
            // The offset-0 block is the executing one.
            Assert.IsTrue(upload.Blocks[2].IsExecuting);
            Assert.AreEqual(0x36, upload.Blocks[2].Message.GetBytes()[0]);
            Assert.AreEqual(0x80, upload.Blocks[2].Message.GetBytes()[1]);
        }

        [TestMethod]
        public void E38_LaunchAck_Is99()
        {
            var protocol = new E38KernelUploadProtocol();
            Assert.IsTrue(protocol.IsExecutingBlockAck(new byte[] { 0x99 }));
            Assert.IsFalse(protocol.IsExecutingBlockAck(new byte[] { 0x76 }));
        }

        // ---- P05c (oddball) ----

        [TestMethod]
        public void P05c_SingleBlock_CopyAtLiteralAddrThenBareExecute()
        {
            var protocol = new P05cKernelUploadProtocol();
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x01 };

            CanKernelUpload upload = protocol.BuildUpload(Gmlan, payload, 0x00FF6000, 0x00FF6000, maxBlockSize: 4096);

            Assert.AreEqual(2, upload.Blocks.Count);

            // All code in a copy block placed at the literal load address - no +4 seam.
            AssertCopyBlock(upload.Blocks[0], 0x00FF6000, payload);

            // Bare execute frame: 0x36 0x80 + address only (no run-address, no code).
            Assert.IsTrue(upload.Blocks[1].IsExecuting);
            CollectionAssert.AreEqual(new byte[] { 0x36, 0x80, 0x00, 0xFF, 0x60, 0x00 }, upload.Blocks[1].Message.GetBytes());

            // RequestDownload declares the raw code size (5) with a 16-bit size field.
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x05 }, upload.RequestDownload.GetBytes());
        }

        [TestMethod]
        public void P05c_MultiBlock_CopiesAscendingAtLiteralAddresses()
        {
            var protocol = new P05cKernelUploadProtocol();
            // blockSize = maxBlockSize - 6 = 4. 10 bytes -> 4 + 4 + 2, then the bare execute frame.
            byte[] payload = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();

            CanKernelUpload upload = protocol.BuildUpload(Gmlan, payload, 0x00FF6000, 0x00FF6000, maxBlockSize: 10);

            Assert.AreEqual(4, upload.Blocks.Count);
            AssertCopyBlock(upload.Blocks[0], 0x00FF6000, new byte[] { 0, 1, 2, 3 });
            AssertCopyBlock(upload.Blocks[1], 0x00FF6004, new byte[] { 4, 5, 6, 7 });
            AssertCopyBlock(upload.Blocks[2], 0x00FF6008, new byte[] { 8, 9 });
            Assert.IsTrue(upload.Blocks[3].IsExecuting);

            // Size field still announces the raw 10 code bytes.
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x0A }, upload.RequestDownload.GetBytes());
        }

        [TestMethod]
        public void P05c_LaunchAck_Accepts76Or99()
        {
            // The boot loader acks the execute frame with 0x76 and jumps; the kernel then announces
            // with 0x99. Either byte confirms the launch.
            var protocol = new P05cKernelUploadProtocol();
            Assert.IsTrue(protocol.IsExecutingBlockAck(new byte[] { 0x76 }));
            Assert.IsTrue(protocol.IsExecutingBlockAck(new byte[] { 0x99 }));
            Assert.IsFalse(protocol.IsExecutingBlockAck(new byte[] { 0x7F }));
        }

        // ---- Helper ----

        private static void AssertCopyBlock(CanUploadBlock block, uint address, byte[] code)
        {
            Assert.IsFalse(block.IsExecuting);
            var expected = new List<byte>
            {
                0x36, 0x00,
                (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            };
            expected.AddRange(code);
            CollectionAssert.AreEqual(expected.ToArray(), block.Message.GetBytes());
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// Reading which generic PIDs a module supports.
    /// </summary>
    [TestClass]
    public class SupportedPidTests
    {
        [TestMethod]
        public void Request_IsAddressedToTheEmissionsModules()
        {
            // Functional, like the other generic services: asked physically it is refused.
            CollectionAssert.AreEqual(
                new byte[] { 0x68, 0x6A, 0xF0, 0x01, 0x00 },
                new Protocol().CreateSupportedPidsRequest(0x00).GetBytes());
        }

        [TestMethod]
        public void Reply_FromHardwareDecodesToTheExpectedPids()
        {
            // Captured from a bench PCM: 41 00 BF BF F9 90.
            Protocol protocol = new Protocol();

            Assert.IsTrue(protocol.TryParseSupportedPids(
                new Message(new byte[] { 0x48, 0x6B, 0x10, 0x41, 0x00, 0xBF, 0xBF, 0xF9, 0x90 }),
                0x00,
                out uint mask));

            Assert.AreEqual(0xBFBFF990u, mask);

            // The ones the dash is built from.
            foreach (uint pid in new uint[] { 0x04, 0x05, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11 })
            {
                Assert.IsTrue(Protocol.MaskIncludes(0x00, mask, pid), $"PID {pid:X2} should be supported");
            }

            // And the ones it genuinely lacks, which is what makes the mask worth asking for.
            foreach (uint pid in new uint[] { 0x02, 0x0A, 0x16, 0x17, 0x1A, 0x1B })
            {
                Assert.IsFalse(Protocol.MaskIncludes(0x00, mask, pid), $"PID {pid:X2} should be absent");
            }
        }

        [TestMethod]
        public void Mask_HighBitIsTheLowestPidInTheBlock()
        {
            // Bit 31 set, nothing else: only the first PID of the block.
            Assert.IsTrue(Protocol.MaskIncludes(0x00, 0x80000000u, 0x01));
            Assert.IsFalse(Protocol.MaskIncludes(0x00, 0x80000000u, 0x02));

            // Bit 0 set is the top of the block, which is also the next block's own number.
            Assert.IsTrue(Protocol.MaskIncludes(0x00, 0x00000001u, 0x20));
            Assert.IsTrue(Protocol.MaskPromisesNextBlock(0x00, 0x00000001u));
            Assert.IsFalse(Protocol.MaskPromisesNextBlock(0x00, 0x80000000u));
        }

        [TestMethod]
        public void Mask_IgnoresPidsOutsideItsBlock()
        {
            // The second block covers 21 to 40, so it says nothing about 01 either way.
            Assert.IsFalse(Protocol.MaskIncludes(0x20, 0xFFFFFFFFu, 0x01));
            Assert.IsTrue(Protocol.MaskIncludes(0x20, 0xFFFFFFFFu, 0x21));
            Assert.IsTrue(Protocol.MaskIncludes(0x20, 0xFFFFFFFFu, 0x40));
            Assert.IsFalse(Protocol.MaskIncludes(0x20, 0xFFFFFFFFu, 0x41));
        }

        [TestMethod]
        public void Reply_ForAnotherBlockIsNotMistakenForThisOne()
        {
            // The block number is echoed, and a reply about PIDs 21-40 must not be read as 01-20.
            Assert.IsFalse(new Protocol().TryParseSupportedPids(
                new Message(new byte[] { 0x48, 0x6B, 0x10, 0x41, 0x20, 0xFF, 0xFF, 0xFF, 0xFF }),
                0x00,
                out uint _));
        }

        [TestMethod]
        public void Reply_ToADifferentServiceIsIgnored()
        {
            // A trouble-code list arriving mid-scan is not a support mask.
            Assert.IsFalse(new Protocol().TryParseSupportedPids(
                new Message(new byte[] { 0x48, 0x6B, 0x10, 0x47, 0x00, 0x00, 0x00, 0x00, 0x00 }),
                0x00,
                out uint _));
        }
    }
}

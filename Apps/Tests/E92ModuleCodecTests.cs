// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The run-length coding the E92 boot loader accepts. The decode vectors are lifted straight out of
    /// a captured vendor write, so they prove we read the vendor's encoding and not just our own.
    /// </summary>
    [TestClass]
    public class E92ModuleCodecTests
    {
        // The first 46 coded bytes of the captured System module (part 12675364), and the 97 bytes of
        // flash they expand to at 0x040000. Exercises the literal run and the one byte fill.
        private static readonly byte[] VendorCoded =
        {
            0x0D, 0xB2, 0xC6, 0x81, 0x02, 0x20, 0x00, 0xC1, 0x69, 0x24, 0x41, 0x47,
            0xFF, 0xFF, 0x43, 0x00, 0x08, 0x31, 0x32, 0x36, 0x37, 0x35, 0x33, 0x36,
            0x34, 0x48, 0x00, 0x02, 0xCE, 0x0D, 0x46, 0xFF, 0x04, 0x17, 0x17, 0x00,
            0x0E, 0x44, 0xFF, 0x02, 0xAA, 0x55, 0x4E, 0xFF, 0x61, 0x00,
        };

        private static readonly byte[] VendorDecoded =
        {
            0xB2, 0xC6, 0x81, 0x02, 0x20, 0x00, 0xC1, 0x69, 0x24, 0x41, 0x47, 0xFF,
            0xFF, 0x00, 0x00, 0x00, 0x31, 0x32, 0x36, 0x37, 0x35, 0x33, 0x36, 0x34,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xCE, 0x0D, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0x17, 0x17, 0x00, 0x0E, 0xFF, 0xFF, 0xFF, 0xFF,
            0xAA, 0x55, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00,
        };

        [TestMethod]
        public void Decompress_ReadsTheVendorEncoding()
        {
            CollectionAssert.AreEqual(VendorDecoded, E92ModuleCodec.Decompress(VendorCoded));
        }

        [TestMethod]
        public void Decompress_ExpandsTwoAndFourByteRepeats()
        {
            // 0x84 = four copies of the next two bytes; 0xC2 = two copies of the next four.
            CollectionAssert.AreEqual(
                new byte[] { 0x00, 0x0C, 0x00, 0x0C, 0x00, 0x0C, 0x00, 0x0C },
                E92ModuleCodec.Decompress(new byte[] { 0x84, 0x00, 0x0C }));

            CollectionAssert.AreEqual(
                new byte[] { 0x3F, 0x7E, 0x35, 0x3F, 0x3F, 0x7E, 0x35, 0x3F },
                E92ModuleCodec.Decompress(new byte[] { 0xC2, 0x3F, 0x7E, 0x35, 0x3F }));
        }

        [TestMethod]
        public void Decompress_StopsAtAZeroControlByte()
        {
            CollectionAssert.AreEqual(
                new byte[] { 0xAA, 0xBB },
                E92ModuleCodec.Decompress(new byte[] { 0x02, 0xAA, 0xBB, 0x00, 0x02, 0xCC, 0xDD }));
        }

        [TestMethod]
        public void Decompress_RejectsATruncatedStream()
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => E92ModuleCodec.Decompress(new byte[] { 0x08, 0x01, 0x02 }));
            Assert.ThrowsException<InvalidOperationException>(
                () => E92ModuleCodec.Decompress(new byte[] { 0xC4, 0x01, 0x02 }));
        }

        [TestMethod]
        public void Compress_RoundTripsEveryShape()
        {
            foreach (byte[] sample in Samples())
            {
                byte[] coded = E92ModuleCodec.Compress(sample);
                CollectionAssert.AreEqual(sample, E92ModuleCodec.Decompress(coded), "Round trip failed for " + sample.Length + " bytes.");
            }
        }

        [TestMethod]
        public void Compress_MatchesTheVendorOnDataTheVendorCoded()
        {
            // The coding is not unique - vendor builds of one calibration differ by a few dozen bytes -
            // but on this run of literals and fills the greedy choice is the only sensible one.
            CollectionAssert.AreEqual(VendorCoded, E92ModuleCodec.Compress(VendorDecoded));
        }

        [TestMethod]
        public void Compress_IsSmallerOnRepetitiveData()
        {
            byte[] table = Enumerable.Range(0, 4096).Select(i => (byte)(i % 4 == 0 ? 0x3F : 0x80)).ToArray();
            Assert.IsTrue(E92ModuleCodec.Compress(table).Length < table.Length / 4);
        }

        [TestMethod]
        public void Wrap_DescribesItsOwnPayload()
        {
            byte[] payload = { 0x02, 0xAA, 0x55 };
            byte[] module = E92ModuleCodec.Wrap(payload);

            Assert.AreEqual(E92ModuleCodec.WrapperLength + payload.Length, module.Length);
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, module.Take(4).ToArray());
            Assert.IsTrue(E92ModuleCodec.IsWrapped(module));
            CollectionAssert.AreEqual(payload, E92ModuleCodec.Unwrap(module));
        }

        [TestMethod]
        public void IsWrapped_RejectsPlainModules()
        {
            // A slave library image starts with its own segment header, not the wrapper.
            byte[] plain = { 0xDE, 0x4F, 0x20, 0x07, 0x20, 0x00, 0xC1, 0x4A, 0x58, 0x41, 0x42, 0xFF, 0xFF };
            Assert.IsFalse(E92ModuleCodec.IsWrapped(plain));
            Assert.IsFalse(E92ModuleCodec.IsWrapped(Array.Empty<byte>()));

            // Right marker, wrong length: not a module.
            byte[] wrongLength = E92ModuleCodec.Wrap(new byte[] { 0x01, 0x02 });
            wrongLength[11] = 0x09;
            Assert.IsFalse(E92ModuleCodec.IsWrapped(wrongLength));
        }

        private static byte[][] Samples()
        {
            var random = new Random(92);
            byte[] noise = new byte[5000];
            random.NextBytes(noise);

            byte[] longRun = Enumerable.Repeat((byte)0xFF, 10000).ToArray();
            byte[] words = Enumerable.Range(0, 2000).SelectMany(_ => new byte[] { 0x00, 0x0C }).ToArray();
            byte[] floats = Enumerable.Range(0, 1000).SelectMany(_ => new byte[] { 0x3F, 0x80, 0x00, 0x00 }).ToArray();
            byte[] pairs = { 0x11, 0x11, 0x22, 0x22, 0x33, 0x33 };

            return new[] { Array.Empty<byte>(), new byte[] { 0x01 }, pairs, noise, longRun, words, floats, VendorDecoded };
        }
    }
}

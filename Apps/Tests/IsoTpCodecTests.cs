// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the ISO 15765-2 (ISO-TP) codec. Positive paths cover encoding,
    /// decoding and round-tripping of valid messages (including the E38 short First Frame
    /// and consecutive-frame sequence wrap); negative paths cover bad arguments and
    /// malformed/out-of-order frames.
    /// </summary>
    [TestClass]
    public class IsoTpCodecTests
    {
        // ── positive: encode ──────────────────────────────────────────────────────

        [TestMethod]
        public void Encode_ThreeBytePayload_IsOnePaddedSingleFrame()
        {
            var frames = new IsoTpCodec().Encode(new byte[] { 0x22, 0xF1, 0x90 }).ToList();

            Assert.AreEqual(1, frames.Count, "Short payload should be a single frame.");
            CollectionAssert.AreEqual(
                new byte[] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA },
                frames[0],
                "Single Frame should carry the length in the PCI nibble and pad to 8 bytes with 0xAA.");
        }

        [TestMethod]
        public void Encode_SevenBytePayload_IsSingleFrame()
        {
            var frames = new IsoTpCodec().Encode(new byte[] { 1, 2, 3, 4, 5, 6, 7 }).ToList();

            Assert.AreEqual(1, frames.Count, "Seven bytes is the largest single frame.");
            Assert.AreEqual(0x07, frames[0][0], "PCI byte should be 0x07 (single frame, 7 data bytes).");
        }

        [TestMethod]
        public void Encode_EightBytePayload_IsFirstFrameThenOneConsecutiveFrame()
        {
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8 };

            var frames = new IsoTpCodec().Encode(payload).ToList();

            Assert.AreEqual(2, frames.Count, "Eight bytes needs a First Frame plus one Consecutive Frame.");

            // First Frame: 0x10 0x08 then first 6 payload bytes.
            CollectionAssert.AreEqual(new byte[] { 0x10, 0x08, 1, 2, 3, 4, 5, 6 }, frames[0], "First Frame layout.");
            // Consecutive Frame #1: 0x21 then the remaining 2 bytes, padded.
            CollectionAssert.AreEqual(new byte[] { 0x21, 7, 8, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA }, frames[1], "Consecutive Frame layout.");
        }

        [TestMethod]
        public void Encode_FirstFrame_EncodesTwelveBitLength()
        {
            // 0x123 = 291 bytes spans both First Frame length bytes (0x1_1 0x23).
            byte[] payload = Enumerable.Range(0, 0x123).Select(i => (byte)i).ToArray();

            byte[] firstFrame = new IsoTpCodec().Encode(payload).First();

            Assert.AreEqual(0x11, firstFrame[0], "High length nibble in the PCI byte.");
            Assert.AreEqual(0x23, firstFrame[1], "Low length byte.");
        }

        // ── positive: decode ──────────────────────────────────────────────────────

        [TestMethod]
        public void Decode_SingleFrame_ReturnsPayload()
        {
            byte[]? result =new IsoTpCodec().FeedFrame(new byte[] { 0x03, 0x7E, 0x01, 0x02, 0xAA, 0xAA, 0xAA, 0xAA });

            CollectionAssert.AreEqual(new byte[] { 0x7E, 0x01, 0x02 }, result, "Single Frame payload.");
        }

        [TestMethod]
        public void Decode_FirstFrameThenConsecutiveFrames_Reassembles()
        {
            var codec = new IsoTpCodec();

            Assert.IsNull(codec.FeedFrame(new byte[] { 0x10, 0x08, 1, 2, 3, 4, 5, 6 }), "First Frame is incomplete.");
            byte[]? result =codec.FeedFrame(new byte[] { 0x21, 7, 8, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA });

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, result, "Reassembled payload.");
        }

        [TestMethod]
        public void Decode_ShortFirstFrame_E38ReadBlock_Reassembles()
        {
            // The E38 read-block First Frame is DLC 7 (5 data bytes), not the usual 6.
            var codec = new IsoTpCodec();

            // Total length 9; First Frame carries only 5 data bytes.
            Assert.IsNull(codec.FeedFrame(new byte[] { 0x10, 0x09, 1, 2, 3, 4, 5 }), "Short First Frame is incomplete.");
            byte[]? result =codec.FeedFrame(new byte[] { 0x21, 6, 7, 8, 9, 0xAA, 0xAA, 0xAA });

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, result, "Reassembled across a short First Frame.");
        }

        // ── positive: round trip ─────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_VariousLengths_PreservePayload()
        {
            // Includes 1024 (a kernel read block) and lengths that exercise sequence wrap.
            foreach (int length in new[] { 1, 2, 7, 8, 13, 63, 64, 113, 256, 1024 })
            {
                byte[] payload = Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 1)).ToArray();
                byte[] roundTripped = RoundTrip(payload);
                CollectionAssert.AreEqual(payload, roundTripped, $"Round trip failed for length {length}.");
            }
        }

        [TestMethod]
        public void RoundTrip_SequenceNumberWraps_PreservesPayload()
        {
            // 6 (First Frame) + 15*7 = 111 fills sequence 1..F; 130 bytes forces a wrap to 0 and beyond.
            byte[] payload = Enumerable.Range(0, 130).Select(i => (byte)i).ToArray();

            CollectionAssert.AreEqual(payload, RoundTrip(payload), "Payload should survive a consecutive-frame sequence wrap.");
        }

        // ── positive: flow control ───────────────────────────────────────────────

        [TestMethod]
        public void MakeFlowControl_Default_IsContinueToSend()
        {
            CollectionAssert.AreEqual(
                new byte[] { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                new IsoTpCodec().MakeFlowControl(),
                "Default Flow Control is ContinueToSend, block size 0, STmin 0.");
        }

        [TestMethod]
        public void MakeFlowControl_SetsBlockSizeAndStMin()
        {
            byte[] fc = new IsoTpCodec().MakeFlowControl(blockSize: 0x08, stMin: 0xF2);

            Assert.AreEqual(0x30, fc[0], "Flow status byte.");
            Assert.AreEqual(0x08, fc[1], "Block size.");
            Assert.AreEqual(0xF2, fc[2], "STmin.");
        }

        // ── positive/negative: frame classification ──────────────────────────────

        [TestMethod]
        public void FrameType_ClassifiesEachFrameType()
        {
            Assert.AreEqual(IsoTpFrameType.SingleFrame, IsoTpCodec.FrameType(new byte[] { 0x03, 1, 2, 3 }));
            Assert.AreEqual(IsoTpFrameType.FirstFrame, IsoTpCodec.FrameType(new byte[] { 0x10, 0x08, 1, 2 }));
            Assert.AreEqual(IsoTpFrameType.ConsecutiveFrame, IsoTpCodec.FrameType(new byte[] { 0x21, 1, 2, 3 }));
            Assert.AreEqual(IsoTpFrameType.FlowControl, IsoTpCodec.FrameType(new byte[] { 0x30, 0, 0 }));
        }

        [TestMethod]
        public void FrameType_EmptyOrReservedPci_IsUnknown()
        {
            Assert.AreEqual(IsoTpFrameType.Unknown, IsoTpCodec.FrameType(new byte[0]), "Empty frame.");
            Assert.AreEqual(IsoTpFrameType.Unknown, IsoTpCodec.FrameType(null!), "Null frame.");
            Assert.AreEqual(IsoTpFrameType.Unknown, IsoTpCodec.FrameType(new byte[] { 0x40, 0, 0 }), "Reserved PCI nibble 0x4.");
        }

        // ── negative: encode arguments ───────────────────────────────────────────

        [TestMethod]
        public void Encode_NullPayload_Throws()
        {
            AssertThrows<ArgumentNullException>(() => new IsoTpCodec().Encode(null!));
        }

        [TestMethod]
        public void Encode_EmptyPayload_Throws()
        {
            AssertThrows<ArgumentException>(() => new IsoTpCodec().Encode(new byte[0]));
        }

        [TestMethod]
        public void Encode_PayloadOverClassicLimit_Throws()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => new IsoTpCodec().Encode(new byte[4096]));
        }

        [TestMethod]
        public void Encode_ArgumentsValidatedEagerly_NotOnEnumeration()
        {
            // The validation must run when Encode is called, not deferred to enumeration.
            AssertThrows<ArgumentNullException>(() => { var _ = new IsoTpCodec().Encode(null!); });
        }

        // ── negative: decode malformed frames ────────────────────────────────────

        [TestMethod]
        public void Decode_NullFrame_ReturnsNull()
        {
            Assert.IsNull(new IsoTpCodec().FeedFrame(null!));
        }

        [TestMethod]
        public void Decode_EmptyFrame_ReturnsNull()
        {
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[0]));
        }

        [TestMethod]
        public void Decode_SingleFrameWithZeroLength_ReturnsNull()
        {
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x00, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA }));
        }

        [TestMethod]
        public void Decode_SingleFrameLengthBeyondFrame_ReturnsNull()
        {
            // PCI claims 7 data bytes but the frame only holds 3.
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x07, 0x01, 0x02, 0x03 }));
        }

        [TestMethod]
        public void Decode_ConsecutiveFrameWithoutFirstFrame_ReturnsNull()
        {
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x21, 1, 2, 3, 4, 5, 6, 7 }));
        }

        [TestMethod]
        public void Decode_RuntFirstFrame_ReturnsNull()
        {
            // A First Frame's length needs two bytes; a one-byte frame can't supply them.
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x10 }));
        }

        [TestMethod]
        public void Decode_FirstFrameZeroLength_ReturnsNull()
        {
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x10, 0x00, 1, 2, 3, 4, 5, 6 }));
        }

        [TestMethod]
        public void Decode_FlowControlFrame_ReturnsNull()
        {
            // Flow Control is handled by the send side; the decoder ignores it.
            Assert.IsNull(new IsoTpCodec().FeedFrame(new byte[] { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        }

        [TestMethod]
        public void Decode_OutOfSequenceConsecutiveFrame_AbandonsMessage()
        {
            var codec = new IsoTpCodec();

            // Start a 3-frame message, then jump the sequence number (expected 0x21, send 0x22).
            Assert.IsNull(codec.FeedFrame(new byte[] { 0x10, 0x14, 1, 2, 3, 4, 5, 6 }), "First Frame, total length 0x14 = 20.");
            Assert.IsNull(codec.FeedFrame(new byte[] { 0x22, 9, 9, 9, 9, 9, 9, 9 }), "Wrong sequence number should be rejected.");

            // Reassembly was abandoned: the correctly-numbered next frame must not complete a
            // (corrupt) message - it now looks like a consecutive frame with no First Frame.
            Assert.IsNull(codec.FeedFrame(new byte[] { 0x23, 7, 8, 9, 10, 11, 12, 13 }), "No payload should emerge from the abandoned message.");
        }

        // ── extended addressing ──────────────────────────────────────────────────

        [TestMethod]
        public void Encode_Extended_SingleFrame_MatchesGmlanProgrammingRequest()
        {
            // The GMLAN all-nodes programming request as the factory tool sends it: 101 FE 02 A5 01.
            var frames = new IsoTpCodec()
                .Encode(new byte[] { 0xA5, 0x01 }, IsoTpAddressing.Extended(CanId.GmlanAllNodesExtension))
                .ToList();

            Assert.AreEqual(1, frames.Count, "Two payload bytes still fit one frame under extended addressing.");
            CollectionAssert.AreEqual(
                new byte[] { 0xFE, 0x02, 0xA5, 0x01, 0xAA, 0xAA, 0xAA, 0xAA },
                frames[0],
                "Address extension leads, then the PCI byte, then the service.");
        }

        [TestMethod]
        public void Encode_Extended_SixBytePayload_IsTheLargestSingleFrame()
        {
            var frames = new IsoTpCodec()
                .Encode(new byte[] { 1, 2, 3, 4, 5, 6 }, IsoTpAddressing.Extended(0xFE))
                .ToList();

            Assert.AreEqual(1, frames.Count, "The extension byte costs one byte, so six is the single-frame maximum.");
            Assert.AreEqual(0x06, frames[0][1], "PCI byte follows the extension and carries the length.");
        }

        [TestMethod]
        public void Encode_Extended_SevenBytePayload_NeedsMultipleFrames()
        {
            // Seven bytes is a single frame under normal addressing but no longer fits under extended.
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7 };

            var frames = new IsoTpCodec().Encode(payload, IsoTpAddressing.Extended(0xFE)).ToList();

            Assert.AreEqual(2, frames.Count, "Seven bytes no longer fits one extended-addressed frame.");
            CollectionAssert.AreEqual(
                new byte[] { 0xFE, 0x10, 0x07, 1, 2, 3, 4, 5 },
                frames[0],
                "First Frame carries five data bytes under extended addressing.");
            CollectionAssert.AreEqual(
                new byte[] { 0xFE, 0x21, 6, 7, 0xAA, 0xAA, 0xAA, 0xAA },
                frames[1],
                "Consecutive Frame carries up to six data bytes under extended addressing.");
        }

        [TestMethod]
        public void RoundTrip_Extended_VariousLengths_PreservePayload()
        {
            IsoTpAddressing extended = IsoTpAddressing.Extended(0xFE);
            foreach (int length in new[] { 1, 2, 6, 7, 13, 96, 113, 1024 })
            {
                byte[] payload = Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 1)).ToArray();
                CollectionAssert.AreEqual(
                    payload,
                    RoundTrip(payload, extended),
                    $"Extended-addressed round trip failed for length {length}.");
            }
        }

        [TestMethod]
        public void Decode_Extended_DifferentExtension_IsNotOurConversation()
        {
            // Same CAN id, different address extension: another conversation, not ours.
            byte[]? result = new IsoTpCodec()
                .FeedFrame(new byte[] { 0xFD, 0x02, 0xA5, 0x01, 0xAA, 0xAA, 0xAA, 0xAA }, IsoTpAddressing.Extended(0xFE));

            Assert.IsNull(result, "A frame carrying another extension must not be decoded as ours.");
        }

        [TestMethod]
        public void Decode_Extended_NormalFrameNotMistakenForExtended()
        {
            // A normally-addressed frame read as extended would treat its PCI byte as the extension.
            Assert.IsNull(
                new IsoTpCodec().FeedFrame(new byte[] { 0x02, 0xA5, 0x01, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA }, IsoTpAddressing.Extended(0xFE)),
                "Normal-addressed frame should not decode on an extended-addressed conversation.");
        }

        [TestMethod]
        public void MakeFlowControl_Extended_PutsExtensionBeforeFlowStatus()
        {
            byte[] fc = new IsoTpCodec().MakeFlowControl(blockSize: 0x08, stMin: 0xF2, addressing: IsoTpAddressing.Extended(0xFE));

            Assert.AreEqual(0xFE, fc[0], "Address extension.");
            Assert.AreEqual(0x30, fc[1], "Flow status byte.");
            Assert.AreEqual(0x08, fc[2], "Block size.");
            Assert.AreEqual(0xF2, fc[3], "STmin.");
        }

        [TestMethod]
        public void FrameType_Extended_ReadsPciAfterTheExtension()
        {
            IsoTpAddressing extended = IsoTpAddressing.Extended(0xFE);

            Assert.AreEqual(IsoTpFrameType.SingleFrame, IsoTpCodec.FrameType(new byte[] { 0xFE, 0x03, 1, 2, 3 }, extended));
            Assert.AreEqual(IsoTpFrameType.FirstFrame, IsoTpCodec.FrameType(new byte[] { 0xFE, 0x10, 0x08, 1 }, extended));
            Assert.AreEqual(IsoTpFrameType.FlowControl, IsoTpCodec.FrameType(new byte[] { 0xFE, 0x30, 0, 0 }, extended));
        }

        [TestMethod]
        public void Addressing_DefaultIsNormal()
        {
            Assert.IsFalse(default(IsoTpAddressing).IsExtended, "The default addressing must be normal.");
            Assert.AreEqual(0, default(IsoTpAddressing).HeaderLength, "Normal addressing reserves no leading byte.");
            Assert.AreEqual(1, IsoTpAddressing.Extended(0xFE).HeaderLength, "Extended addressing reserves one leading byte.");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Encode a payload, then feed every frame back through a fresh decoder (sending Flow
        /// Control after the First Frame, exactly as a device would) and return the result.
        /// </summary>
        private static byte[] RoundTrip(byte[] payload, IsoTpAddressing addressing = default)
        {
            var encoder = new IsoTpCodec();
            var decoder = new IsoTpCodec();

            byte[]? result =null;
            List<byte[]> frames = encoder.Encode(payload, addressing).ToList();
            for (int i = 0; i < frames.Count; i++)
            {
                byte[]? assembled = decoder.FeedFrame(frames[i], addressing);
                if (assembled != null)
                {
                    result = assembled;
                }
                // After a First Frame the receiver would emit Flow Control; building it here keeps
                // the test honest about the handshake even though the decoder doesn't consume it.
                if (i == 0 && frames.Count > 1)
                {
                    decoder.MakeFlowControl(addressing: addressing);
                }
            }

            Assert.IsNotNull(result, "Round trip produced no reassembled payload.");
            return result!;
        }

        private static void AssertThrows<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Assert.AreEqual(typeof(T), ex.GetType(), $"Expected {typeof(T).Name} but got {ex.GetType().Name}.");
                return;
            }

            Assert.Fail($"Expected {typeof(T).Name} but no exception was thrown.");
        }
    }
}

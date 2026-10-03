// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The USB-CAN Analyzer's wire format, checked against the worked examples in the vendor's
    /// protocol document ("USB (Serial port) to CAN protocol defines").
    /// </summary>
    /// <remarks>
    /// The adapter acknowledges nothing it is sent, so a malformed frame looks exactly like a silent
    /// bus. These examples are the only way to check the format without the hardware in hand.
    /// </remarks>
    [TestClass]
    public class UsbCanAnalyzerProtocolTests
    {
        /// <summary>
        /// The vendor's first 20-byte example: standard id 0x123, data 11..88, stated checksum 0x93
        /// with the sum shown as 0x293. Pins down the byte range, which the prose ("from frame type
        /// to error code") does not identify on its own.
        /// </summary>
        [TestMethod]
        public void Checksum_MatchesVendorStandardFrameExample()
        {
            byte[] frame =
            {
                0xAA, 0x55, 0x01, 0x01, 0x01, 0x23, 0x01, 0x00, 0x00, 0x08,
                0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x00, 0x00,
            };

            Assert.AreEqual(0x93, UsbCanAnalyzerProtocol.Checksum(frame));
        }

        /// <summary>The vendor's second example: extended id 0x12345678, data 01..08, checksum 0x44.</summary>
        [TestMethod]
        public void Checksum_MatchesVendorExtendedFrameExample()
        {
            byte[] frame =
            {
                0xAA, 0x55, 0x01, 0x02, 0x01, 0x78, 0x56, 0x34, 0x12, 0x08,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x00, 0x00,
            };

            Assert.AreEqual(0x44, UsbCanAnalyzerProtocol.Checksum(frame));
        }

        [TestMethod]
        public void BuildDataFrame_StandardEightBytes_MatchesVendorExample()
        {
            byte[] expected =
            {
                0xAA, 0xC8, 0x23, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x55,
            };

            byte[] actual = UsbCanAnalyzerProtocol.BuildDataFrame(
                0x123, new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 });

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void BuildDataFrame_StandardTwoBytes_MatchesVendorExample()
        {
            byte[] expected = { 0xAA, 0xC2, 0x03, 0x01, 0x11, 0x22, 0x55 };

            byte[] actual = UsbCanAnalyzerProtocol.BuildDataFrame(0x103, new byte[] { 0x11, 0x22 });

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void BuildDataFrame_ExtendedEightBytes_MatchesVendorExample()
        {
            byte[] expected =
            {
                0xAA, 0xE8, 0x67, 0x45, 0x23, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x55,
            };

            byte[] actual = UsbCanAnalyzerProtocol.BuildDataFrame(
                0x1234567, new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 });

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void BuildDataFrame_ExtendedTwoBytes_MatchesVendorExample()
        {
            byte[] expected = { 0xAA, 0xE2, 0x21, 0x30, 0x03, 0x01, 0x11, 0x22, 0x55 };

            byte[] actual = UsbCanAnalyzerProtocol.BuildDataFrame(0x1033021, new byte[] { 0x11, 0x22 });

            CollectionAssert.AreEqual(expected, actual);
        }

        /// <summary>
        /// An id that will not fit 11 bits has to go out as an extended frame regardless of what the
        /// caller asked for, or the adapter would silently truncate it.
        /// </summary>
        [TestMethod]
        public void BuildDataFrame_IdAboveElevenBits_UsesExtendedFraming()
        {
            byte[] frame = UsbCanAnalyzerProtocol.BuildDataFrame(0x800, new byte[] { 0x01 });

            Assert.AreEqual(
                UsbCanAnalyzerProtocol.ExtendedIdFlag,
                (byte)(frame[1] & UsbCanAnalyzerProtocol.ExtendedIdFlag),
                "extended flag");
            Assert.AreEqual(8, frame.Length, "4-byte id, 1 data byte, 3 framing bytes");
        }

        [TestMethod]
        public void BuildDataFrame_PayloadLongerThanEight_IsTruncated()
        {
            byte[] frame = UsbCanAnalyzerProtocol.BuildDataFrame(
                0x7E0, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

            Assert.AreEqual(8, frame[1] & 0x0F, "declared length");
            Assert.AreEqual(13, frame.Length, "2-byte id, 8 data bytes, 3 framing bytes");
            Assert.AreEqual(UsbCanAnalyzerProtocol.FrameEnd, frame[frame.Length - 1], "terminator");
        }

        [TestMethod]
        public void BuildConfiguration_HasTheDocumentedShape()
        {
            byte[] command = UsbCanAnalyzerProtocol.BuildConfiguration(
                UsbCanAnalyzerProtocol.WorkModeNormal);

            Assert.AreEqual(20, command.Length, "length");
            Assert.AreEqual(UsbCanAnalyzerProtocol.FrameStart, command[0], "header 1");
            Assert.AreEqual(UsbCanAnalyzerProtocol.FrameEnd, command[1], "header 2");
            Assert.AreEqual(UsbCanAnalyzerProtocol.ModeVariableLength, command[2], "protocol select");
            Assert.AreEqual(UsbCanAnalyzerProtocol.Bitrate500k, command[3], "bitrate");
            Assert.AreEqual(UsbCanAnalyzerProtocol.WorkModeNormal, command[13], "work mode");
            Assert.AreEqual(UsbCanAnalyzerProtocol.AutoRetransmitEnabled, command[14], "retransmit");
            Assert.AreEqual(UsbCanAnalyzerProtocol.Checksum(command), command[19], "checksum");

            // Filter and mask zeroed, so nothing is filtered out in hardware.
            for (int index = 5; index <= 12; index++)
            {
                Assert.AreEqual(0, command[index], "filter/mask byte " + index);
            }

            // Reserved bytes.
            for (int index = 15; index <= 18; index++)
            {
                Assert.AreEqual(0, command[index], "reserved byte " + index);
            }
        }

        [TestMethod]
        public void BuildConfiguration_LoopbackSilentSelectsThatMode()
        {
            byte[] command = UsbCanAnalyzerProtocol.BuildConfiguration(
                UsbCanAnalyzerProtocol.WorkModeLoopbackSilent);

            Assert.AreEqual(UsbCanAnalyzerProtocol.WorkModeLoopbackSilent, command[13]);
            Assert.AreEqual(UsbCanAnalyzerProtocol.Checksum(command), command[19]);
        }

        [TestMethod]
        public void BuiltFrames_RoundTripThroughTheParser()
        {
            byte[] payload = { 0x02, 0x10, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 };
            byte[] frame = UsbCanAnalyzerProtocol.BuildDataFrame(0x7E0, payload);

            List<CanMessage> messages = Parse(frame);

            Assert.AreEqual(1, messages.Count, "message count");
            Assert.AreEqual((uint)0x7E0, messages[0].MessageId, "id");
            CollectionAssert.AreEqual(payload, messages[0].Payload.ToArray(), "payload");
        }

        /// <summary>
        /// A configuration command begins AA 55, and 0x55 read as a type byte would claim an extended
        /// remote frame of length 5. The parser has to reject it on the type byte's top bits and stay
        /// in step with the frame that follows.
        /// </summary>
        [TestMethod]
        public void Parser_IgnoresACommandEchoAndStaysInSync()
        {
            List<byte> stream = new List<byte>();
            stream.AddRange(UsbCanAnalyzerProtocol.BuildConfiguration(UsbCanAnalyzerProtocol.WorkModeNormal));
            stream.AddRange(UsbCanAnalyzerProtocol.BuildDataFrame(0x7E8, new byte[] { 0x41, 0x42 }));

            List<CanMessage> messages = Parse(stream.ToArray());

            Assert.AreEqual(1, messages.Count, "only the data frame is a message");
            Assert.AreEqual((uint)0x7E8, messages[0].MessageId, "id");
        }

        /// <summary>A frame without its terminator is discarded: the bytes after it are not the payload we counted.</summary>
        [TestMethod]
        public void Parser_DiscardsAFrameWithABadTerminator()
        {
            byte[] stream = { 0xAA, 0xC2, 0x03, 0x01, 0x11, 0x22, 0x99 };

            Assert.AreEqual(0, Parse(stream).Count);
        }

        [TestMethod]
        public void Parser_IgnoresJunkBetweenFrames()
        {
            List<byte> stream = new List<byte> { 0x00, 0xFF, 0x12, 0x7E };
            stream.AddRange(UsbCanAnalyzerProtocol.BuildDataFrame(0x123, new byte[] { 0x01 }));
            stream.AddRange(new byte[] { 0x00, 0x99 });
            stream.AddRange(UsbCanAnalyzerProtocol.BuildDataFrame(0x124, new byte[] { 0x02 }));

            List<CanMessage> messages = Parse(stream.ToArray());

            Assert.AreEqual(2, messages.Count, "message count");
            Assert.AreEqual((uint)0x123, messages[0].MessageId, "first id");
            Assert.AreEqual((uint)0x124, messages[1].MessageId, "second id");
        }

        [TestMethod]
        public void Checksum_RejectsAShortFrame()
        {
            Assert.ThrowsException<ArgumentException>(
                () => UsbCanAnalyzerProtocol.Checksum(new byte[19]));
        }

        private static List<CanMessage> Parse(byte[] stream)
        {
            CanParser parser = new CanParser();
            List<CanMessage> messages = new List<CanMessage>();

            foreach (byte value in stream)
            {
                if (parser.IsCompleteMessage(value, out CanMessage message))
                {
                    messages.Add(message);
                }
            }

            return messages;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Tests for the software ISO-TP transport (IsoTpTransport + IsoTpCodec over an ICanChannel):
    /// single- and multi-frame send/receive, the Flow Control handshake, and CAN-ID filtering. A
    /// fake channel supplies/records raw CAN frames so the ISO-TP behaviour is exercised without
    /// hardware.
    /// </summary>
    [TestClass]
    public class IsoTpTransportTests
    {
        private const uint Tx = 0x7E0;
        private const uint Rx = 0x7E8;

        // ── positive: send ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task SendMessage_SingleFrame_SendsOneFrameOnTxId()
        {
            var channel = new FakeCanChannel();
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(new byte[] { 0x22, 0xF1, 0x90 }));

            Assert.IsTrue(ok);
            Assert.AreEqual(1, channel.Sent.Count, "A short payload is one Single Frame.");
            Assert.AreEqual(Tx, channel.Sent[0].id, "Sent on the Tx CAN ID.");
            CollectionAssert.AreEqual(new byte[] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA }, channel.Sent[0].frame);
        }

        [TestMethod]
        public async Task SendMessage_MultiFrame_WithFlowControl_SendsFirstThenConsecutive()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(Rx, 0x30, 0, 0, 0, 0, 0, 0, 0); // Flow Control from the ECU
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.IsTrue(ok);
            Assert.AreEqual(2, channel.Sent.Count, "First Frame then one Consecutive Frame.");
            CollectionAssert.AreEqual(new byte[] { 0x10, 0x08, 1, 2, 3, 4, 5, 6 }, channel.Sent[0].frame, "First Frame.");
            CollectionAssert.AreEqual(new byte[] { 0x21, 7, 8, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA }, channel.Sent[1].frame, "Consecutive Frame.");
        }

        // ── positive: receive ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task ReceiveMessage_SingleFrameOnRxId_ReturnsMessage()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(Rx, 0x03, 0x7E, 0x01, 0x02, 0xAA, 0xAA, 0xAA, 0xAA);
            var transport = new IsoTpTransport(channel);

            Message? received = await transport.ReceiveMessage();

            Assert.IsNotNull(received);
            CollectionAssert.AreEqual(new byte[] { 0x7E, 0x01, 0x02 }, received!.GetBytes());
        }

        [TestMethod]
        public async Task ReceiveMessage_MultiFrame_SendsFlowControlThenReassembles()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(Rx, 0x10, 0x08, 1, 2, 3, 4, 5, 6);            // First Frame
            channel.QueueIncoming(Rx, 0x21, 7, 8, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA); // Consecutive Frame
            var transport = new IsoTpTransport(channel);

            // One receive reassembles the whole message: it reads the First Frame (sending a Flow
            // Control so the ECU may continue), then the Consecutive Frame, and returns the payload.
            Message? complete = await transport.ReceiveMessage();
            Assert.IsNotNull(complete);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, complete!.GetBytes());
            Assert.AreEqual(1, channel.Sent.Count, "A Flow Control should have been sent.");
            Assert.AreEqual(Tx, channel.Sent[0].id, "Flow Control goes out on the Tx CAN ID.");
            Assert.AreEqual(IsoTpFrameType.FlowControl, IsoTpCodec.FrameType(channel.Sent[0].frame));
        }

        // ── negative ──────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task SendMessage_MultiFrame_NoFlowControl_ReturnsFalseAndStopsAfterFirstFrame()
        {
            var channel = new FakeCanChannel(); // no Flow Control queued
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.IsFalse(ok, "Without Flow Control the multi-frame send fails.");
            Assert.AreEqual(1, channel.Sent.Count, "Only the First Frame is sent; no Consecutive Frames.");
        }

        [TestMethod]
        public async Task ReceiveMessage_FrameOnWrongCanId_IsIgnored()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(0x123, 0x03, 0x7E, 0x01, 0x02, 0xAA, 0xAA, 0xAA, 0xAA);
            var transport = new IsoTpTransport(channel);

            Message? received = await transport.ReceiveMessage();

            Assert.IsNull(received, "A frame on a non-Rx CAN ID is ignored.");
            Assert.AreEqual(0, channel.Sent.Count, "Nothing is transmitted in response to an ignored frame.");
        }

        [TestMethod]
        public async Task ReceiveMessage_NoTraffic_ReturnsNull()
        {
            var transport = new IsoTpTransport(new FakeCanChannel()); // ReceiveCanFrame returns a timeout

            Assert.IsNull(await transport.ReceiveMessage());
        }

        [TestMethod]
        public async Task SendMessage_MultiFrame_FlowControlOverflow_ReturnsFalse()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(Rx, 0x32, 0, 0, 0, 0, 0, 0, 0); // Flow Control, FlowStatus = Overflow
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.IsFalse(ok, "An Overflow Flow Control aborts the transfer.");
            Assert.AreEqual(1, channel.Sent.Count, "Only the First Frame is sent.");
        }

        [TestMethod]
        public async Task SendMessage_MultiFrame_FlowControlWaitThenContinue_Sends()
        {
            var channel = new FakeCanChannel();
            channel.QueueIncoming(Rx, 0x31, 0, 0, 0, 0, 0, 0, 0); // Flow Control, FlowStatus = Wait
            channel.QueueIncoming(Rx, 0x30, 0, 0, 0, 0, 0, 0, 0); // Flow Control, ContinueToSend
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.IsTrue(ok, "Wait should be ridden out until ContinueToSend arrives.");
            Assert.AreEqual(2, channel.Sent.Count, "First Frame then the Consecutive Frame.");
        }

        [TestMethod]
        public async Task SendMessage_MultiFrame_BlockSizeOne_ReWaitsFlowControlPerFrame()
        {
            // 25 bytes -> First Frame (6) + three Consecutive Frames (7 + 7 + 5).
            byte[] payload = new byte[25];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i + 1);

            var channel = new FakeCanChannel();
            // Block size 1 means a fresh Flow Control is required before each Consecutive Frame
            // except the last (the loop ends before re-waiting after the final frame).
            channel.QueueIncoming(Rx, 0x30, 0x01, 0x00, 0, 0, 0, 0, 0);
            channel.QueueIncoming(Rx, 0x30, 0x01, 0x00, 0, 0, 0, 0, 0);
            channel.QueueIncoming(Rx, 0x30, 0x01, 0x00, 0, 0, 0, 0, 0);
            var transport = new IsoTpTransport(channel);

            bool ok = await transport.SendMessage(new Message(payload));

            Assert.IsTrue(ok);
            Assert.AreEqual(4, channel.Sent.Count, "First Frame plus three Consecutive Frames.");
        }

        [TestMethod]
        public void Constructor_NullChannel_Throws()
        {
            try
            {
                var _ = new IsoTpTransport(null!);
            }
            catch (ArgumentNullException)
            {
                return;
            }

            Assert.Fail("Expected ArgumentNullException for a null channel.");
        }

        // ── fake hardware ─────────────────────────────────────────────────────────

        /// <summary>
        /// An ICanChannel backed by in-memory queues: SendCanFrame records transmitted frames,
        /// ReceiveCanFrame returns queued frames (or a timeout when empty).
        /// </summary>
        private sealed class FakeCanChannel : ICanChannel
        {
            public uint TxCanId { get; }
            public uint RxCanId { get; }
            public int ReceiveTimeoutMilliseconds => 1000;
            public readonly List<(uint id, byte[] frame)> Sent = new List<(uint id, byte[] frame)>();
            private readonly Queue<(uint id, byte[] frame)> incoming = new Queue<(uint id, byte[] frame)>();

            public FakeCanChannel(uint tx = Tx, uint rx = Rx)
            {
                this.TxCanId = tx;
                this.RxCanId = rx;
            }

            public void QueueIncoming(uint id, params byte[] frame) => this.incoming.Enqueue((id, frame));

            public Task SendCanFrame(uint canId, byte[] framePayload)
            {
                this.Sent.Add((canId, (byte[])framePayload.Clone()));
                return Task.CompletedTask;
            }

            public Task<(uint id, byte[] frame)> ReceiveCanFrame()
            {
                return Task.FromResult(this.incoming.Count > 0
                    ? this.incoming.Dequeue()
                    : (0u, Array.Empty<byte>()));
            }
        }
    }
}

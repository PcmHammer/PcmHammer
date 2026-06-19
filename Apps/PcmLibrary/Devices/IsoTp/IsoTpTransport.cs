// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Raw CAN frame I/O a device exposes so the software ISO-TP transport can run over it. A device
    /// with native ISO-TP (OBDX HSCAN, J2534 ISO15765) does not use this; one needing software ISO-TP
    /// (AVT with framing off, SLCAN) implements it and composes an <see cref="IsoTpTransport"/>.
    /// </summary>
    public interface ICanChannel
    {
        /// <summary>CAN ID used when sending to the ECU (tool to ECU).</summary>
        uint TxCanId { get; }

        /// <summary>CAN ID the ECU answers on; frames with any other ID are ignored.</summary>
        uint RxCanId { get; }

        /// <summary>Send one CAN frame (up to 8 data bytes) with the given CAN ID.</summary>
        Task SendCanFrame(uint canId, byte[] framePayload);

        /// <summary>Block until one CAN frame arrives or the device times out; return (0, empty) on timeout.</summary>
        Task<(uint id, byte[] frame)> ReceiveCanFrame();
    }

    /// <summary>
    /// Software ISO 15765-2 (ISO-TP) transport over an <see cref="ICanChannel"/>: segments and
    /// reassembles whole payloads (as Message objects) and drives the Flow Control handshake,
    /// honouring the ECU's block size and separation time. Touches only the channel and
    /// <see cref="IsoTpCodec"/>.
    /// </summary>
    public class IsoTpTransport
    {
        private readonly ICanChannel channel;
        private readonly IsoTpCodec codec = new IsoTpCodec();

        // STmin we request when an ECU streams a multi-frame message to us. 0xF1 = 100us, the ISO-TP
        // minimum: paces the ECU without measurably slowing a large read.
        private const byte ReadBlockStMin = 0xF1;

        // Bounds the wait for a Flow Control (it normally arrives on the first read).
        private const int MaxFlowControlAttempts = 100;

        public IsoTpTransport(ICanChannel channel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        /// <summary>
        /// Send a full payload, segmenting into ISO-TP frames. A single-frame message goes out
        /// immediately; a multi-frame message sends the First Frame, waits for the ECU's Flow
        /// Control, then sends the Consecutive Frames paced by the requested separation time and
        /// re-waiting for Flow Control after each block. Returns false if Flow Control never
        /// arrives or the ECU aborts the transfer.
        /// </summary>
        public async Task<bool> SendMessage(Message message)
        {
            List<byte[]> frames = new List<byte[]>(this.codec.Encode(message.GetBytes()));

            await this.channel.SendCanFrame(this.channel.TxCanId, frames[0]);
            if (frames.Count == 1)
            {
                return true;
            }

            FlowControl fc = await this.WaitForFlowControl();
            if (!fc.Ok)
            {
                return false;
            }

            // Pace Consecutive Frames at the ECU's separation time with a high-resolution spin.
            // Task.Delay's ~15 ms floor would stretch a large transfer (e.g. a kernel upload) to
            // many seconds, long enough for the ECU to abandon it.
            long ticksPerFrame = StMinTicks(fc.StMinMicros);
            long nextSendTick = Stopwatch.GetTimestamp();
            int framesInBlock = 0;

            for (int i = 1; i < frames.Count; i++)
            {
                if (ticksPerFrame > 0)
                {
                    nextSendTick += ticksPerFrame;
                    while (Stopwatch.GetTimestamp() < nextSendTick)
                    {
                        Thread.SpinWait(32);
                    }
                }

                await this.channel.SendCanFrame(this.channel.TxCanId, frames[i]);

                if (i == frames.Count - 1)
                {
                    break;
                }

                if (fc.BlockSize > 0 && ++framesInBlock >= fc.BlockSize)
                {
                    // End of this block: the ECU sends another Flow Control before we may continue.
                    fc = await this.WaitForFlowControl();
                    if (!fc.Ok)
                    {
                        return false;
                    }
                    framesInBlock = 0;
                    ticksPerFrame = StMinTicks(fc.StMinMicros);
                    nextSendTick = Stopwatch.GetTimestamp();
                }
            }

            return true;
        }

        // Safety cap so a flooded bus can't hang the receive; far above the largest classic ISO-TP
        // message (~586 frames). Normal exit is a completed message or the per-frame receive timeout.
        private const int MaxFramesPerMessage = 4096;

        /// <summary>
        /// Receive one whole ISO-TP message, returning it once reassembly completes or null when the
        /// per-frame read times out. A Flow Control is sent on the First Frame so the ECU streams the
        /// rest. Frames on a different CAN ID (e.g. a transmit-echo) are skipped.
        /// </summary>
        public async Task<Message?> ReceiveMessage()
        {
            for (int framesRead = 0; framesRead < MaxFramesPerMessage; framesRead++)
            {
                (uint id, byte[] frame) incoming = await this.channel.ReceiveCanFrame();
                if (incoming.frame.Length == 0)
                {
                    // Device read timed out: no (more) frames available.
                    return null;
                }

                if (incoming.id != this.channel.RxCanId)
                {
                    // Not this conversation; keep reading for ours.
                    continue;
                }

                if (IsoTpCodec.FrameType(incoming.frame) == IsoTpFrameType.FirstFrame)
                {
                    await this.channel.SendCanFrame(this.channel.TxCanId, this.codec.MakeFlowControl(blockSize: 0, stMin: ReadBlockStMin));
                }

                byte[]? assembled = this.codec.FeedFrame(incoming.frame);
                if (assembled != null)
                {
                    return new Message(assembled);
                }
                // Intermediate frame: keep reading until the message completes or the device times out.
            }

            return null;
        }

        private async Task<FlowControl> WaitForFlowControl()
        {
            for (int attempt = 0; attempt < MaxFlowControlAttempts; attempt++)
            {
                (uint id, byte[] frame) incoming = await this.channel.ReceiveCanFrame();
                if (incoming.id != this.channel.RxCanId ||
                    incoming.frame.Length < 3 ||
                    IsoTpCodec.FrameType(incoming.frame) != IsoTpFrameType.FlowControl)
                {
                    continue;
                }

                int flowStatus = incoming.frame[0] & 0x0F;
                if (flowStatus == 0x01)   // Wait: keep listening for ContinueToSend
                {
                    continue;
                }
                if (flowStatus == 0x02)   // Overflow / abort
                {
                    return FlowControl.Fail;
                }
                return FlowControl.ContinueToSend(incoming.frame[1], DecodeStMinMicros(incoming.frame[2]));
            }

            return FlowControl.Fail;
        }

        private static long StMinTicks(int stMinMicros)
            => stMinMicros > 0 ? (long)(Stopwatch.Frequency * (stMinMicros / 1_000_000.0)) : 0;

        /// <summary>
        /// Decode an ISO-TP STmin byte: 0x00-0x7F is milliseconds (x1000); 0xF1-0xF9 is 100-900 microseconds.
        /// </summary>
        private static int DecodeStMinMicros(byte raw)
        {
            if (raw <= 0x7F) return raw * 1000;
            if (raw >= 0xF1 && raw <= 0xF9) return (raw - 0xF0) * 100;
            return 0;
        }

        /// <summary>Decoded Flow Control: whether to proceed, and the ECU's block size / separation time.</summary>
        private readonly struct FlowControl
        {
            public bool Ok { get; }
            public int BlockSize { get; }
            public int StMinMicros { get; }

            private FlowControl(bool ok, int blockSize, int stMinMicros)
            {
                this.Ok = ok;
                this.BlockSize = blockSize;
                this.StMinMicros = stMinMicros;
            }

            public static readonly FlowControl Fail = new FlowControl(false, 0, 0);

            public static FlowControl ContinueToSend(int blockSize, int stMinMicros)
                => new FlowControl(true, blockSize, stMinMicros);
        }
    }
}

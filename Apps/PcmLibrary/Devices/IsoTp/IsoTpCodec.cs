// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// ISO 15765-2 (ISO-TP) frame type, taken from the high nibble of the PCI byte.
    /// </summary>
    public enum IsoTpFrameType
    {
        SingleFrame      = 0x0,
        FirstFrame       = 0x1,
        ConsecutiveFrame = 0x2,
        FlowControl      = 0x3,
        Unknown          = 0xF,
    }

    /// <summary>
    /// Encode and decode ISO 15765-2 (ISO-TP) frames over classic 8-byte CAN. <see cref="Encode"/>
    /// segments a payload into CAN frames to transmit; <see cref="FeedFrame"/> reassembles incoming
    /// frames. The class never touches a device - <see cref="MakeFlowControl"/> returns the Flow
    /// Control frame for the caller to send. One instance tracks one in-flight reassembly.
    /// </summary>
    public class IsoTpCodec
    {
        // ── frame geometry ───────────────────────────────────────────────────────
        private const int  CanFrameSize  = 8;
        private const byte Padding       = 0xAA;   // GM testers pad unused trailing bytes with 0xAA

        private const int MaxSingleFrameLength       = 7;       // 1 PCI byte + up to 7 data bytes
        private const int FirstFrameDataLength       = 6;       // 2 PCI bytes + 6 data bytes
        private const int ConsecutiveFrameDataLength = 7;       // 1 PCI byte + up to 7 data bytes
        private const int MaxClassicLength           = 0x0FFF;  // 12-bit First Frame length field = 4095

        // Flow Control PCI byte: 0x30 = ContinueToSend (FlowStatus 0).
        private const byte FlowControlContinue = 0x30;

        // ── receive (reassembly) state ───────────────────────────────────────────
        private int    expectedLength;
        private int    receivedLength;
        private byte   expectedSequence = 1;
        private byte[] receiveBuffer    = Array.Empty<byte>();

        // ── frame classification ─────────────────────────────────────────────────

        /// <summary>
        /// Classify a CAN frame payload by its ISO-TP PCI type. Returns
        /// <see cref="IsoTpFrameType.Unknown"/> for an empty frame or a reserved PCI value.
        /// </summary>
        public static IsoTpFrameType FrameType(byte[] frame)
        {
            if (frame == null || frame.Length == 0)
            {
                return IsoTpFrameType.Unknown;
            }

            switch ((frame[0] & 0xF0) >> 4)
            {
                case (int)IsoTpFrameType.SingleFrame:      return IsoTpFrameType.SingleFrame;
                case (int)IsoTpFrameType.FirstFrame:       return IsoTpFrameType.FirstFrame;
                case (int)IsoTpFrameType.ConsecutiveFrame: return IsoTpFrameType.ConsecutiveFrame;
                case (int)IsoTpFrameType.FlowControl:      return IsoTpFrameType.FlowControl;
                default:                                   return IsoTpFrameType.Unknown;
            }
        }

        // ── transmit ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Segment a full payload into one or more 8-byte CAN frame payloads. A payload of up to
        /// 7 bytes is a single Single Frame; a larger one is a First Frame followed by Consecutive
        /// Frames. The caller transmits them in order and handles Flow Control between the First
        /// Frame and the Consecutive Frames.
        /// </summary>
        /// <exception cref="ArgumentNullException">payload is null.</exception>
        /// <exception cref="ArgumentException">payload is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">payload exceeds the 4095-byte ISO-TP classic limit.</exception>
        public IEnumerable<byte[]> Encode(byte[] payload)
        {
            // Validate eagerly: the iterator below would otherwise defer these throws until the
            // caller starts enumerating.
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (payload.Length == 0)
            {
                throw new ArgumentException("An ISO-TP message must contain at least one byte.", nameof(payload));
            }
            if (payload.Length > MaxClassicLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    $"Payload of {payload.Length} bytes exceeds the ISO-TP classic limit of {MaxClassicLength}.");
            }

            return EncodeFrames(payload);
        }

        private IEnumerable<byte[]> EncodeFrames(byte[] payload)
        {
            if (payload.Length <= MaxSingleFrameLength)
            {
                // Single Frame: PCI byte = 0x0N where N is the data length.
                byte[] frame = NewPaddedFrame();
                frame[0] = (byte)(Pci(IsoTpFrameType.SingleFrame) | payload.Length);
                Array.Copy(payload, 0, frame, 1, payload.Length);
                yield return frame;
                yield break;
            }

            // First Frame: PCI = 0x1H 0xLL (12-bit total length), then the first 6 payload bytes.
            byte[] ff = NewPaddedFrame();
            ff[0] = (byte)(Pci(IsoTpFrameType.FirstFrame) | ((payload.Length >> 8) & 0x0F));
            ff[1] = (byte)(payload.Length & 0xFF);
            Array.Copy(payload, 0, ff, 2, FirstFrameDataLength);
            yield return ff;

            // Consecutive Frames: PCI = 0x2N, sequence number 1..F then wrapping to 0.
            int offset = FirstFrameDataLength;
            byte sequence = 1;
            while (offset < payload.Length)
            {
                int dataBytes = Math.Min(ConsecutiveFrameDataLength, payload.Length - offset);
                byte[] cf = NewPaddedFrame();
                cf[0] = (byte)(Pci(IsoTpFrameType.ConsecutiveFrame) | (sequence & 0x0F));
                Array.Copy(payload, offset, cf, 1, dataBytes);
                yield return cf;

                offset += dataBytes;
                sequence = NextSequence(sequence);
            }
        }

        // ── receive ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Feed one incoming 8-byte CAN frame payload into the reassembly state machine. Returns
        /// the complete payload when a Single Frame arrives or the final Consecutive Frame
        /// completes a multi-frame message; returns null while still waiting for more frames or
        /// when the frame is not part of a valid message.
        /// </summary>
        public byte[]? FeedFrame(byte[] canFramePayload)
        {
            switch (FrameType(canFramePayload))
            {
                case IsoTpFrameType.SingleFrame:
                {
                    int length = canFramePayload[0] & 0x0F;
                    // A Single Frame must carry 1..7 bytes that actually fit in the frame.
                    if (length == 0 || length > canFramePayload.Length - 1)
                    {
                        return null;
                    }
                    Reset();
                    return Slice(canFramePayload, 1, length);
                }

                case IsoTpFrameType.FirstFrame:
                {
                    // The length spans two PCI bytes; ignore a runt frame that can't hold them.
                    if (canFramePayload.Length < 2)
                    {
                        return null;
                    }
                    int total = ((canFramePayload[0] & 0x0F) << 8) | canFramePayload[1];
                    if (total == 0)
                    {
                        return null;
                    }
                    Reset();
                    expectedLength   = total;
                    receiveBuffer    = new byte[total];
                    expectedSequence = 1;
                    // A First Frame normally carries 6 data bytes, but some ECUs send a shorter
                    // frame (the E38 read-block First Frame is DLC 7 = 5 data bytes), so clamp to
                    // what the frame and the declared length actually contain.
                    int firstData = Min3(FirstFrameDataLength, total, canFramePayload.Length - 2);
                    Array.Copy(canFramePayload, 2, receiveBuffer, 0, firstData);
                    receivedLength = firstData;
                    return null;
                }

                case IsoTpFrameType.ConsecutiveFrame:
                {
                    if (expectedLength == 0)
                    {
                        return null; // no First Frame yet
                    }
                    byte sequence = (byte)(canFramePayload[0] & 0x0F);
                    if (sequence != expectedSequence)
                    {
                        // A gap or out-of-order frame: abandon the message rather than splice
                        // mismatched data. The caller re-requests cleanly.
                        Reset();
                        return null;
                    }

                    int remaining = expectedLength - receivedLength;
                    int dataBytes = Min3(ConsecutiveFrameDataLength, remaining, canFramePayload.Length - 1);
                    Array.Copy(canFramePayload, 1, receiveBuffer, receivedLength, dataBytes);
                    receivedLength += dataBytes;
                    expectedSequence = NextSequence(expectedSequence);

                    if (receivedLength >= expectedLength)
                    {
                        byte[] result = receiveBuffer;
                        Reset();
                        return result;
                    }
                    return null;
                }

                case IsoTpFrameType.FlowControl:
                case IsoTpFrameType.Unknown:
                default:
                    // Flow Control is handled by the send side; anything else is ignored.
                    return null;
            }
        }

        // ── flow control ─────────────────────────────────────────────────────────

        /// <summary>
        /// Build a Flow Control frame to send after receiving a First Frame. blockSize 0 = send
        /// all remaining frames without another Flow Control; stMin 0 = no minimum separation time.
        /// </summary>
        public byte[] MakeFlowControl(byte blockSize = 0, byte stMin = 0)
        {
            byte[] frame = new byte[CanFrameSize];
            frame[0] = FlowControlContinue;
            frame[1] = blockSize;
            frame[2] = stMin;
            // bytes 3..7 are reserved and left 0.
            return frame;
        }

        /// <summary>Discard any in-progress reassembly state.</summary>
        public void Reset()
        {
            expectedLength   = 0;
            receivedLength   = 0;
            expectedSequence = 1;
            receiveBuffer    = Array.Empty<byte>();
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>The PCI byte (high nibble set) for a frame type; OR in the low-nibble field.</summary>
        private static int Pci(IsoTpFrameType type) => (int)type << 4;

        private static byte[] NewPaddedFrame()
        {
            byte[] frame = new byte[CanFrameSize];
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = Padding;
            }
            return frame;
        }

        private static byte NextSequence(byte sequence) => (byte)((sequence + 1) & 0x0F);

        private static int Min3(int a, int b, int c) => Math.Min(Math.Min(a, b), c);

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(source, offset, result, 0, length);
            return result;
        }
    }
}

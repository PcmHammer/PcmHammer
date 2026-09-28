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
    /// <para>
    /// Every operation takes an <see cref="IsoTpAddressing"/>, defaulting to normal addressing.
    /// Extended addressing reserves the frame's first byte for the address extension, so the PCI and
    /// all data shift along by one and each frame carries one byte less payload.
    /// </para>
    /// </summary>
    public class IsoTpCodec
    {
        // ── frame geometry (normal addressing; extended addressing costs one byte per frame) ─────
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
        public static IsoTpFrameType FrameType(byte[] frame) => FrameType(frame, IsoTpAddressing.Normal);

        /// <summary>
        /// Classify a CAN frame payload whose conversation uses <paramref name="addressing"/>; under
        /// extended addressing the PCI byte sits after the address extension.
        /// </summary>
        public static IsoTpFrameType FrameType(byte[] frame, IsoTpAddressing addressing)
        {
            int header = addressing.HeaderLength;
            if (frame == null || frame.Length <= header)
            {
                return IsoTpFrameType.Unknown;
            }

            switch ((frame[header] & 0xF0) >> 4)
            {
                case (int)IsoTpFrameType.SingleFrame:      return IsoTpFrameType.SingleFrame;
                case (int)IsoTpFrameType.FirstFrame:       return IsoTpFrameType.FirstFrame;
                case (int)IsoTpFrameType.ConsecutiveFrame: return IsoTpFrameType.ConsecutiveFrame;
                case (int)IsoTpFrameType.FlowControl:      return IsoTpFrameType.FlowControl;
                default:                                   return IsoTpFrameType.Unknown;
            }
        }

        /// <summary>
        /// Whether an incoming frame belongs to a conversation using <paramref name="addressing"/>.
        /// Under extended addressing a frame addressed to a different extension is another
        /// conversation sharing the same CAN id, and must not be decoded as ours.
        /// </summary>
        public static bool BelongsTo(byte[] frame, IsoTpAddressing addressing)
        {
            if (frame == null || frame.Length <= addressing.HeaderLength)
            {
                return false;
            }

            return !addressing.IsExtended || frame[0] == addressing.AddressExtension;
        }

        // ── transmit ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Segment a full payload into one or more 8-byte CAN frame payloads. A payload that fits one
        /// frame is a single Single Frame; a larger one is a First Frame followed by Consecutive
        /// Frames. The caller transmits them in order and handles Flow Control between the First
        /// Frame and the Consecutive Frames.
        /// </summary>
        /// <exception cref="ArgumentNullException">payload is null.</exception>
        /// <exception cref="ArgumentException">payload is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">payload exceeds the 4095-byte ISO-TP classic limit.</exception>
        public IEnumerable<byte[]> Encode(byte[] payload, IsoTpAddressing addressing = default)
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

            return EncodeFrames(payload, addressing);
        }

        private IEnumerable<byte[]> EncodeFrames(byte[] payload, IsoTpAddressing addressing)
        {
            int header = addressing.HeaderLength;

            if (payload.Length <= MaxSingleFrameLength - header)
            {
                // Single Frame: PCI byte = 0x0N where N is the data length.
                byte[] frame = NewPaddedFrame(addressing);
                frame[header] = (byte)(Pci(IsoTpFrameType.SingleFrame) | payload.Length);
                Array.Copy(payload, 0, frame, header + 1, payload.Length);
                yield return frame;
                yield break;
            }

            // First Frame: PCI = 0x1H 0xLL (12-bit total length), then the first payload bytes.
            int firstFrameData = FirstFrameDataLength - header;
            byte[] ff = NewPaddedFrame(addressing);
            ff[header]     = (byte)(Pci(IsoTpFrameType.FirstFrame) | ((payload.Length >> 8) & 0x0F));
            ff[header + 1] = (byte)(payload.Length & 0xFF);
            Array.Copy(payload, 0, ff, header + 2, firstFrameData);
            yield return ff;

            // Consecutive Frames: PCI = 0x2N, sequence number 1..F then wrapping to 0.
            int consecutiveData = ConsecutiveFrameDataLength - header;
            int offset = firstFrameData;
            byte sequence = 1;
            while (offset < payload.Length)
            {
                int dataBytes = Math.Min(consecutiveData, payload.Length - offset);
                byte[] cf = NewPaddedFrame(addressing);
                cf[header] = (byte)(Pci(IsoTpFrameType.ConsecutiveFrame) | (sequence & 0x0F));
                Array.Copy(payload, offset, cf, header + 1, dataBytes);
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
        public byte[]? FeedFrame(byte[] canFramePayload, IsoTpAddressing addressing = default)
        {
            if (!BelongsTo(canFramePayload, addressing))
            {
                return null;
            }

            int header = addressing.HeaderLength;

            switch (FrameType(canFramePayload, addressing))
            {
                case IsoTpFrameType.SingleFrame:
                {
                    int length = canFramePayload[header] & 0x0F;
                    // A Single Frame must carry at least one byte, and no more than actually fit.
                    if (length == 0 || length > canFramePayload.Length - header - 1)
                    {
                        return null;
                    }
                    Reset();
                    return Slice(canFramePayload, header + 1, length);
                }

                case IsoTpFrameType.FirstFrame:
                {
                    // The length spans two PCI bytes; ignore a runt frame that can't hold them.
                    if (canFramePayload.Length < header + 2)
                    {
                        return null;
                    }
                    int total = ((canFramePayload[header] & 0x0F) << 8) | canFramePayload[header + 1];
                    if (total == 0)
                    {
                        return null;
                    }
                    Reset();
                    expectedLength   = total;
                    receiveBuffer    = new byte[total];
                    expectedSequence = 1;
                    // A First Frame normally fills the frame, but some ECUs send a shorter one (the
                    // E38 read-block First Frame is DLC 7 = 5 data bytes), so clamp to what the frame
                    // and the declared length actually contain.
                    int firstData = Min3(FirstFrameDataLength - header, total, canFramePayload.Length - header - 2);
                    Array.Copy(canFramePayload, header + 2, receiveBuffer, 0, firstData);
                    receivedLength = firstData;
                    return null;
                }

                case IsoTpFrameType.ConsecutiveFrame:
                {
                    if (expectedLength == 0)
                    {
                        return null; // no First Frame yet
                    }
                    byte sequence = (byte)(canFramePayload[header] & 0x0F);
                    if (sequence != expectedSequence)
                    {
                        // A gap or out-of-order frame: abandon the message rather than splice
                        // mismatched data. The caller re-requests cleanly.
                        Reset();
                        return null;
                    }

                    int remaining = expectedLength - receivedLength;
                    int dataBytes = Min3(ConsecutiveFrameDataLength - header, remaining, canFramePayload.Length - header - 1);
                    Array.Copy(canFramePayload, header + 1, receiveBuffer, receivedLength, dataBytes);
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
        public byte[] MakeFlowControl(byte blockSize = 0, byte stMin = 0, IsoTpAddressing addressing = default)
        {
            int header = addressing.HeaderLength;
            byte[] frame = new byte[CanFrameSize];
            if (addressing.IsExtended)
            {
                frame[0] = addressing.AddressExtension;
            }
            frame[header]     = FlowControlContinue;
            frame[header + 1] = blockSize;
            frame[header + 2] = stMin;
            // Remaining bytes are reserved and left 0.
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

        private static byte[] NewPaddedFrame(IsoTpAddressing addressing)
        {
            byte[] frame = new byte[CanFrameSize];
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = Padding;
            }

            if (addressing.IsExtended)
            {
                frame[0] = addressing.AddressExtension;
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

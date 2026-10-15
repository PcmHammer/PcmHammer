// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// The USB-CAN Analyzer's wire format: builds the frames sent to the adapter.
    /// <see cref="CanParser"/> is the other half, decoding what comes back.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UsbCanAnalyzerDevice"/> so the format can be tested without a port.
    /// That matters more here than usual: the adapter acknowledges nothing, so a malformed frame is
    /// indistinguishable from an absent bus, and the documented examples are the only way to check
    /// this code short of hardware.
    /// </remarks>
    public static class UsbCanAnalyzerProtocol
    {
        /// <summary>Starts every frame in both directions.</summary>
        public const byte FrameStart = 0xAA;

        /// <summary>Ends a data frame; also the second header byte of a command.</summary>
        public const byte FrameEnd = 0x55;

        /// <summary>
        /// Base of a data frame's type byte. The top two bits are always set - the vendor's examples
        /// are C2, C8, E2 and E8 - which is what lets a data frame be told from a command.
        /// </summary>
        public const byte DataFrameType = 0xC0;

        public const byte ExtendedIdFlag = 0x20;

        public const byte RemoteFrameFlag = 0x10;

        public const int ConfigurationFrameLength = 20;

        /// <summary>Byte 2 of a command: select the variable-length protocol (0x02 selects fixed 20-byte).</summary>
        public const byte ModeVariableLength = 0x12;

        public const byte Bitrate500k = 0x03;

        public const byte FrameTypeStandard = 0x01;

        public const byte WorkModeNormal = 0x00;

        /// <summary>
        /// "Hot self test": the controller receives its own frames and only ever sends recessive bits,
        /// so nothing reaches the bus. What makes an identification probe safe on a live vehicle.
        /// </summary>
        public const byte WorkModeLoopbackSilent = 0x03;

        /// <summary>
        /// Byte 14, the vendor's "only send once". 0x00 lets the controller retry a frame it lost to
        /// arbitration, which a request/response exchange wants; 0x01 disables that.
        /// </summary>
        public const byte AutoRetransmitEnabled = 0x00;

        /// <summary>
        /// Build a data frame: start byte, a type byte carrying the payload length and the
        /// extended/remote flags, the id little-endian, the payload, then the end byte. This format
        /// carries no checksum.
        /// </summary>
        public static byte[] BuildDataFrame(uint canId, byte[] framePayload, bool remoteFrame = false)
        {
            if (framePayload == null)
            {
                throw new ArgumentNullException(nameof(framePayload));
            }

            int length = Math.Min(framePayload.Length, 8);

            // An id that does not fit 11 bits has to go out as an extended frame whatever the caller
            // thinks, or the adapter would truncate it.
            bool extended = canId > 0x7FF;
            int idLength = extended ? 4 : 2;

            byte[] frame = new byte[3 + idLength + length];
            int at = 0;

            frame[at++] = FrameStart;
            frame[at++] = (byte)(
                DataFrameType
                | (extended ? ExtendedIdFlag : 0)
                | (remoteFrame ? RemoteFrameFlag : 0)
                | length);

            for (int shift = 0; shift < idLength; shift++)
            {
                frame[at++] = (byte)(canId >> (shift * 8));
            }

            for (int index = 0; index < length; index++)
            {
                frame[at++] = framePayload[index];
            }

            frame[at] = FrameEnd;
            return frame;
        }

        /// <summary>
        /// Build the 20-byte configuration command: variable-length protocol at 500 kbit/s in the
        /// given work mode, with the acceptance filter left open so every frame on the bus arrives.
        /// </summary>
        public static byte[] BuildConfiguration(byte workMode)
        {
            byte[] command = new byte[ConfigurationFrameLength];

            command[0] = FrameStart;
            command[1] = FrameEnd;
            command[2] = ModeVariableLength;
            command[3] = Bitrate500k;

            // Selects the id width the adapter's own filter applies to, which is moot with the filter
            // zeroed below: extended frames still arrive, and each received frame's type byte says
            // which kind it was.
            command[4] = FrameTypeStandard;

            // Bytes 5-8 filter id and 9-12 mask id stay zero, so nothing is filtered out and the
            // ISO-TP layer picks our conversation out of the traffic. Logging wants the whole bus.
            command[13] = workMode;
            command[14] = AutoRetransmitEnabled;

            // Bytes 15-18 are reserved and must be zero.
            command[19] = Checksum(command);
            return command;
        }

        /// <summary>
        /// Low 8 bits of the sum of bytes 2 to 18 of a 20-byte frame.
        /// </summary>
        /// <remarks>
        /// The vendor's prose says "from frame type to error code", which does not identify the range
        /// on its own; both worked examples in the protocol document sum bytes 2 to 18, and that
        /// agrees with the two independent implementations this driver was written against.
        /// </remarks>
        public static byte Checksum(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (frame.Length < ConfigurationFrameLength)
            {
                throw new ArgumentException(
                    "A checksummed frame is " + ConfigurationFrameLength + " bytes.", nameof(frame));
            }

            int sum = 0;
            for (int index = 2; index <= 18; index++)
            {
                sum += frame[index];
            }

            return (byte)(sum & 0xFF);
        }
    }
}

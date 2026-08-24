// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// A compressed flash module: a 12 byte wrapper around a run-length coded segment image, declared
    /// with <see cref="Gmlan.DataFormatCompressed"/> in RequestDownload.
    /// </summary>
    /// <remarks>
    /// The coding runs over 1, 2 and 4 byte units, which suits calibration tables of 8, 16 and 32 bit
    /// values. Each control byte is [unit:2][count:6]:
    /// <list type="bullet">
    /// <item>0b00 - literal run: copy the next <c>count</c> bytes.</item>
    /// <item>0b01 - <c>count</c> copies of the next byte.</item>
    /// <item>0b10 - <c>count</c> copies of the next two bytes.</item>
    /// <item>0b11 - <c>count</c> copies of the next four bytes.</item>
    /// </list>
    /// A zero control byte ends the stream. Encodings are not unique, so a module only has to decode
    /// correctly, not match any particular encoder.
    /// </remarks>
    public static class E92ModuleCodec
    {
        /// <summary>Length of the wrapper that precedes the coded payload.</summary>
        public const int WrapperLength = 12;

        // A control byte holds the count in its low six bits.
        private const int MaxCount = 0x3F;

        // Bytes per repeat, indexed by the control byte's top two bits. 0 marks the literal run.
        private static readonly int[] UnitSize = { 0, 1, 2, 4 };

        /// <summary>
        /// Wrap a coded payload for download: 0xFF filler, a zero word, then the payload length.
        /// </summary>
        public static byte[] Wrap(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] module = new byte[WrapperLength + payload.Length];
            for (int i = 0; i < 4; i++)
            {
                module[i] = 0xFF;
            }

            module[8] = (byte)(payload.Length >> 24);
            module[9] = (byte)(payload.Length >> 16);
            module[10] = (byte)(payload.Length >> 8);
            module[11] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, module, WrapperLength, payload.Length);
            return module;
        }

        /// <summary>
        /// True when these bytes are a wrapped module, which is what tells a module streamed from the
        /// slave library apart from one that is streamed as-is. The length field makes the test exact.
        /// </summary>
        public static bool IsWrapped(byte[] module)
        {
            if (module == null || module.Length < WrapperLength)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (module[i] != 0xFF)
                {
                    return false;
                }
            }

            long declared = ((long)module[8] << 24) | ((long)module[9] << 16) | ((long)module[10] << 8) | module[11];
            return declared == module.Length - WrapperLength;
        }

        /// <summary>The coded payload of a wrapped module. Throws if the bytes are not a module.</summary>
        public static byte[] Unwrap(byte[] module)
        {
            if (!IsWrapped(module))
            {
                throw new InvalidOperationException("These bytes are not a wrapped flash module.");
            }

            byte[] payload = new byte[module.Length - WrapperLength];
            Buffer.BlockCopy(module, WrapperLength, payload, 0, payload.Length);
            return payload;
        }

        /// <summary>
        /// Code a segment image. The result decodes back to <paramref name="data"/> exactly; callers that
        /// are about to program flash should prove that with <see cref="Decompress"/> before sending.
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var coded = new List<byte>(data.Length / 2);
            int literalStart = 0;
            int position = 0;

            while (position < data.Length)
            {
                int bestUnit = 0;
                int bestCount = 0;
                for (int tag = 1; tag < UnitSize.Length; tag++)
                {
                    int unit = UnitSize[tag];
                    int count = RepeatCount(data, position, unit);

                    // One control byte plus the unit has to buy back more than it costs, so a pair of
                    // equal bytes stays a literal (it may yet merge into a longer literal run).
                    if (count * unit <= unit + 1 || count * unit <= bestCount * bestUnit)
                    {
                        continue;
                    }

                    bestUnit = unit;
                    bestCount = count;
                }

                if (bestCount == 0)
                {
                    position++;
                    continue;
                }

                AppendLiterals(coded, data, literalStart, position - literalStart);
                coded.Add((byte)((TagOf(bestUnit) << 6) | bestCount));
                for (int i = 0; i < bestUnit; i++)
                {
                    coded.Add(data[position + i]);
                }

                position += bestCount * bestUnit;
                literalStart = position;
            }

            AppendLiterals(coded, data, literalStart, data.Length - literalStart);
            return coded.ToArray();
        }

        /// <summary>
        /// Decode a coded payload. Throws <see cref="InvalidOperationException"/> if the stream runs off
        /// the end, which is what a truncated or misidentified module looks like.
        /// </summary>
        public static byte[] Decompress(byte[] coded)
        {
            if (coded == null)
            {
                throw new ArgumentNullException(nameof(coded));
            }

            var data = new List<byte>(coded.Length * 2);
            int position = 0;

            while (position < coded.Length)
            {
                byte control = coded[position++];
                if (control == 0)
                {
                    break;
                }

                int count = control & MaxCount;
                int unit = UnitSize[control >> 6];
                int available = coded.Length - position;

                if (unit == 0)
                {
                    if (count > available)
                    {
                        throw new InvalidOperationException("Flash module ends inside a literal run.");
                    }

                    for (int i = 0; i < count; i++)
                    {
                        data.Add(coded[position + i]);
                    }

                    position += count;
                    continue;
                }

                if (unit > available)
                {
                    throw new InvalidOperationException("Flash module ends inside a repeat.");
                }

                for (int repeat = 0; repeat < count; repeat++)
                {
                    for (int i = 0; i < unit; i++)
                    {
                        data.Add(coded[position + i]);
                    }
                }

                position += unit;
            }

            return data.ToArray();
        }

        /// <summary>How many whole <paramref name="unit"/> sized groups at <paramref name="position"/> repeat the first one.</summary>
        private static int RepeatCount(byte[] data, int position, int unit)
        {
            if (position + unit > data.Length)
            {
                return 0;
            }

            int count = 1;
            while (count < MaxCount && position + ((count + 1) * unit) <= data.Length)
            {
                int next = position + (count * unit);
                int i = 0;
                while (i < unit && data[next + i] == data[position + i])
                {
                    i++;
                }

                if (i < unit)
                {
                    break;
                }

                count++;
            }

            return count > 1 ? count : 0;
        }

        private static void AppendLiterals(List<byte> coded, byte[] data, int start, int length)
        {
            while (length > 0)
            {
                int take = Math.Min(length, MaxCount);
                coded.Add((byte)take);
                for (int i = 0; i < take; i++)
                {
                    coded.Add(data[start + i]);
                }

                start += take;
                length -= take;
            }
        }

        private static int TagOf(int unit) => unit == 1 ? 1 : unit == 2 ? 2 : 3;
    }
}

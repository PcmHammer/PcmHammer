// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// One flash segment of a master image: inclusive start and end, plus the segment name. HeaderOffset
    /// is where the segment's own header record sits, which is what the OS module leads with.
    /// </summary>
    public readonly struct FlashSegment
    {
        public int Start { get; }
        public int End { get; }
        public string Name { get; }
        public int HeaderOffset { get; }
        public int Length => this.End - this.Start + 1;

        public FlashSegment(int start, int end, string name, int headerOffset = 0)
        {
            this.Start = start;
            this.End = end;
            this.Name = name;
            this.HeaderOffset = headerOffset;
        }
    }

    /// <summary>
    /// One module as the boot loader receives it: the bytes to stream, how RequestDownload declares them,
    /// and how many leading bytes are a header the boot loader parses before the rest arrives.
    /// </summary>
    public sealed class FlashModule
    {
        public string Name { get; }

        /// <summary>Bytes to stream, header included.</summary>
        public byte[] Data { get; }

        /// <summary>
        /// Leading bytes the boot loader parses on their own. They are sent as a message of their own,
        /// because a header merged into a larger message is rejected. 0 when there is no header.
        /// </summary>
        public int HeaderLength { get; }

        /// <summary>RequestDownload data format identifier: 0x00 as-is, 0x10 compressed.</summary>
        public byte DataFormat { get; }

        public FlashModule(string name, byte[] data, int headerLength = 0, byte dataFormat = Gmlan.DataFormatUncompressed)
        {
            this.Name = name;
            this.Data = data ?? throw new ArgumentNullException(nameof(data));
            this.HeaderLength = headerLength;
            this.DataFormat = dataFormat;
        }
    }

    /// <summary>
    /// Splits a master image into the flash modules that <see cref="CanBootLoaderWriter"/> streams to
    /// the boot loader: one module per flash segment, with the OS module led by a copy of its header.
    /// </summary>
    public static class FlashModuleBuilder
    {
        /// <summary>
        /// Build the flash modules for a master image. Pass false for
        /// <paramref name="includeOperatingSystem"/> to build a calibration-only download, which leaves
        /// the OS segment alone. Throws <see cref="InvalidOperationException"/> if the PCM has no known
        /// segment layout, or the image does not match it.
        /// </summary>
        public static List<FlashModule> Build(byte[] masterImage, OSIDInfo pcmInfo, bool includeOperatingSystem = true)
        {
            if (masterImage == null)
            {
                throw new ArgumentNullException(nameof(masterImage));
            }

            List<FlashSegment> segments = GetSegments(masterImage, pcmInfo);
            int headerLength = pcmInfo.BootLoaderMasterHeaderLength;
            var modules = new List<FlashModule>(segments.Count);

            for (int i = 0; i < segments.Count; i++)
            {
                if (i == 0 && !includeOperatingSystem)
                {
                    continue;
                }

                FlashSegment segment = segments[i];

                if (segment.Start < 0 || segment.End >= masterImage.Length || segment.Length <= 0)
                {
                    throw new InvalidOperationException(string.Format(
                        "Segment {0} ({1}) is outside the image: 0x{2:X6}-0x{3:X6}.", i + 1, segment.Name, segment.Start, segment.End));
                }

                // The first segment is the OS. Its module leads with a copy of the segment's own header,
                // which the boot loader parses before the module data arrives.
                if (i == 0)
                {
                    if (segment.Length < segment.HeaderOffset + headerLength)
                    {
                        throw new InvalidOperationException("The operating system segment is too short to build an OS module.");
                    }

                    byte[] module = new byte[headerLength + segment.Length];
                    Buffer.BlockCopy(masterImage, segment.Start + segment.HeaderOffset, module, 0, headerLength);
                    Buffer.BlockCopy(masterImage, segment.Start, module, headerLength, segment.Length);
                    modules.Add(new FlashModule(segment.Name, module, headerLength));
                }
                else if (pcmInfo.BootLoaderModuleDataFormat == Gmlan.DataFormatCompressed)
                {
                    modules.Add(new FlashModule(
                        segment.Name, Compress(masterImage, segment), 0, pcmInfo.BootLoaderModuleDataFormat));
                }
                else
                {
                    byte[] module = new byte[segment.Length];
                    Buffer.BlockCopy(masterImage, segment.Start, module, 0, segment.Length);
                    modules.Add(new FlashModule(segment.Name, module));
                }
            }

            return modules;
        }

        /// <summary>
        /// Describe slave images from the library as modules. They are already in module form, so this
        /// only says how each one is streamed: a wrapped image is declared compressed and carries no
        /// header, and the first plain image leads with the slave OS header the boot loader parses.
        /// </summary>
        public static List<FlashModule> BuildSlaveModules(IList<byte[]> slaveImages, OSIDInfo pcmInfo)
        {
            if (slaveImages == null)
            {
                throw new ArgumentNullException(nameof(slaveImages));
            }

            var modules = new List<FlashModule>(slaveImages.Count);
            for (int i = 0; i < slaveImages.Count; i++)
            {
                string name = i < pcmInfo.SlaveModules.Count ? pcmInfo.SlaveModules[i].Target : "slave module " + (i + 1);
                modules.Add(E92ModuleCodec.IsWrapped(slaveImages[i])
                    ? new FlashModule(name, slaveImages[i], 0, Gmlan.DataFormatCompressed)
                    : new FlashModule(name, slaveImages[i], i == 0 ? pcmInfo.BootLoaderSlaveHeaderLength : 0));
            }

            return modules;
        }

        /// <summary>
        /// Code one segment into a module, then decode it again and check it against the source. The
        /// module is about to be burned into a PCM, so the round trip is worth the few milliseconds.
        /// </summary>
        private static byte[] Compress(byte[] masterImage, FlashSegment segment)
        {
            byte[] source = new byte[segment.Length];
            Buffer.BlockCopy(masterImage, segment.Start, source, 0, segment.Length);

            byte[] coded = E92ModuleCodec.Compress(source);
            byte[] decoded = E92ModuleCodec.Decompress(coded);
            if (decoded.Length != source.Length)
            {
                throw new InvalidOperationException("Compressing the " + segment.Name + " segment changed its length.");
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (decoded[i] != source[i])
                {
                    throw new InvalidOperationException(string.Format(
                        "Compressing the {0} segment changed the byte at 0x{1:X6}.", segment.Name, segment.Start + i));
                }
            }

            return E92ModuleCodec.Wrap(coded);
        }

        /// <summary>
        /// The flash segments of a master image, read from wherever this PCM type records them.
        /// </summary>
        private static List<FlashSegment> GetSegments(byte[] masterImage, OSIDInfo pcmInfo)
        {
            switch (pcmInfo.HardwareType)
            {
                case PcmType.E38:
                    return FileValidator.GetE38MasterSegments(masterImage);

                case PcmType.E92:
                    return FileValidator.GetE92MasterSegments(masterImage);

                default:
                    throw new InvalidOperationException(
                        "The flash segment layout of the " + pcmInfo.Description + " is not known.");
            }
        }
    }
}

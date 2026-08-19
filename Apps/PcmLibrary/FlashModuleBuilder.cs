// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// One flash segment of a master image: inclusive start and end, plus the segment name.
    /// </summary>
    public readonly struct FlashSegment
    {
        public int Start { get; }
        public int End { get; }
        public string Name { get; }
        public int Length => this.End - this.Start + 1;

        public FlashSegment(int start, int end, string name)
        {
            this.Start = start;
            this.End = end;
            this.Name = name;
        }
    }

    /// <summary>
    /// Splits a master image into the flash modules that <see cref="CanBootLoaderWriter"/> streams to
    /// the boot loader: one module per flash segment, with the OS module led by a copy of its header.
    /// </summary>
    public static class FlashModuleBuilder
    {
        /// <summary>
        /// Build the flash modules for a master image. Throws <see cref="InvalidOperationException"/> if
        /// the PCM has no known segment layout, or the image does not match it.
        /// </summary>
        public static List<byte[]> Build(byte[] masterImage, OSIDInfo pcmInfo)
        {
            if (masterImage == null)
            {
                throw new ArgumentNullException(nameof(masterImage));
            }

            List<FlashSegment> segments = GetSegments(masterImage, pcmInfo);
            int headerLength = pcmInfo.BootLoaderMasterHeaderLength;
            var modules = new List<byte[]>(segments.Count);

            for (int i = 0; i < segments.Count; i++)
            {
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
                    if (segment.Length < headerLength)
                    {
                        throw new InvalidOperationException("The operating system segment is too short to build an OS module.");
                    }

                    byte[] module = new byte[headerLength + segment.Length];
                    Buffer.BlockCopy(masterImage, segment.Start, module, 0, headerLength);
                    Buffer.BlockCopy(masterImage, segment.Start, module, headerLength, segment.Length);
                    modules.Add(module);
                }
                else
                {
                    byte[] module = new byte[segment.Length];
                    Buffer.BlockCopy(masterImage, segment.Start, module, 0, segment.Length);
                    modules.Add(module);
                }
            }

            return modules;
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

                default:
                    throw new InvalidOperationException(
                        "The flash segment layout of the " + pcmInfo.Description + " is not known.");
            }
        }
    }
}

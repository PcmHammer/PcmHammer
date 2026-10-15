// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    public partial class Protocol
    {
        /// <summary>
        /// The blocks of PID numbers a module can be asked about, each covering the 32 PIDs above it.
        /// </summary>
        /// <remarks>
        /// Only the generic range is enumerable this way. The manufacturer's own PIDs - everything
        /// above 0x1000 here - have no equivalent mask, so a module's answer says nothing about
        /// them either way.
        /// </remarks>
        public static readonly byte[] SupportedPidBlocks = { 0x00, 0x20, 0x40, 0x60 };

        /// <summary>
        /// Ask which of the 32 PIDs above <paramref name="block"/> the module supports.
        /// </summary>
        /// <remarks>
        /// Functionally addressed, like the other generic services: asked at a module's own address
        /// this is refused, and the reply comes back to the emissions address rather than the tool.
        /// </remarks>
        public Message CreateSupportedPidsRequest(byte block) =>
            new Message(new byte[]
            {
                Priority.Functional0, EmissionsModules, ToolId, Mode.GetCurrentData, block,
            });

        /// <summary>
        /// Read the four-byte support mask out of a reply, as the set of PIDs it marks.
        /// </summary>
        /// <remarks>
        /// The mask is big-endian and the high bit of the first byte is the lowest PID in the
        /// block, so bit 31 means <paramref name="block"/> + 1.
        /// </remarks>
        public bool TryParseSupportedPids(Message message, byte block, out uint mask)
        {
            mask = 0;

            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();

            if (bytes.Length < 9 ||
                (bytes[1] != EmissionsModulesReply && bytes[1] != ToolId) ||
                bytes[3] != (byte)(Mode.GetCurrentData + Mode.Response) ||
                bytes[4] != block)
            {
                return false;
            }

            mask = (uint)((bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8]);
            return true;
        }

        /// <summary>Whether a support mask marks a given PID.</summary>
        public static bool MaskIncludes(byte block, uint mask, uint pid)
        {
            if (pid <= block || pid > block + 32)
            {
                return false;
            }

            int offset = (int)(pid - block);
            return (mask & (1u << (32 - offset))) != 0;
        }

        /// <summary>
        /// Whether a mask says the next block is worth asking for, which is the top block's own PID.
        /// </summary>
        public static bool MaskPromisesNextBlock(byte block, uint mask) =>
            MaskIncludes(block, mask, (uint)(block + 32));
    }
}

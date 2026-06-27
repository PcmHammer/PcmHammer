// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// Which GMLAN protocol variant a CAN PCM speaks for a kernel upload: how it wants an uploaded kernel
    /// announced, framed, placed, and launched. One value per PCM generation. To support a new generation,
    /// add a value here, add a matching <see cref="CanKernelUploadProtocol"/> subclass, wire the two together
    /// in <see cref="CanKernelUploadProtocol.For"/>, and set this on the PCM in PcmInfo.
    /// </summary>
    public enum GMLANProtocol
    {
        /// <summary>Not a CAN PCM, or no GMLAN variant configured yet.</summary>
        None = 0,

        /// <summary>E38-style: executing block carries [run-addr][code], lands at load+4, acked 0x99.</summary>
        E38,

        /// <summary>P05c-style: plain copy blocks at literal addresses, bare execute frame, acked 0x76.</summary>
        P05c,
    }

    /// <summary>One transfer block to send during a kernel upload.</summary>
    public sealed class CanUploadBlock
    {
        public CanUploadBlock(Message message, bool isExecuting)
        {
            this.Message = message;
            this.IsExecuting = isExecuting;
        }

        /// <summary>The framed 0x36 message to send.</summary>
        public Message Message { get; }

        /// <summary>True for the final block that launches the kernel (its ack differs from a copy's 0x76).</summary>
        public bool IsExecuting { get; }
    }

    /// <summary>An ordered upload plan: the RequestDownload that announces it, then the transfer blocks.</summary>
    public sealed class CanKernelUpload
    {
        public CanKernelUpload(Message requestDownload, IReadOnlyList<CanUploadBlock> blocks)
        {
            this.RequestDownload = requestDownload;
            this.Blocks = blocks;
        }

        /// <summary>The 0x34 RequestDownload to send before the blocks.</summary>
        public Message RequestDownload { get; }

        /// <summary>Blocks in send order; the last one is the executing block.</summary>
        public IReadOnlyList<CanUploadBlock> Blocks { get; }
    }

    /// <summary>
    /// Boot-loader RAM-load dialect for uploading a kernel over CAN. Each PCM generation's permanent boot
    /// loader differs in how it wants the kernel announced (0x34), framed and placed (0x36/0x00), and
    /// launched (0x36/0x80), so each gets a subclass selected by <see cref="GMLANProtocol"/>. The command
    /// layer (<see cref="CanCommands"/>) drives the upload and stays dialect-agnostic.
    /// </summary>
    public abstract class CanKernelUploadProtocol
    {
        private static readonly CanKernelUploadProtocol e38 = new E38KernelUploadProtocol();
        private static readonly CanKernelUploadProtocol p05c = new P05cKernelUploadProtocol();

        /// <summary>Resolve the upload protocol for a PCM's GMLAN variant; throws if the PCM has none configured.</summary>
        public static CanKernelUploadProtocol For(GMLANProtocol protocol)
        {
            switch (protocol)
            {
                case GMLANProtocol.E38: return e38;
                case GMLANProtocol.P05c: return p05c;
                default:
                    throw new InvalidOperationException(
                        "No CAN kernel upload protocol is configured for this PCM.");
            }
        }

        /// <summary>
        /// Build the upload plan - the RequestDownload message and the ordered transfer blocks - sized to
        /// <paramref name="maxBlockSize"/> (the device's CAN payload capacity).
        /// </summary>
        public abstract CanKernelUpload BuildUpload(Gmlan gmlan, byte[] payload, uint loadAddress, uint runAddress, int maxBlockSize);

        /// <summary>Does <paramref name="response"/> acknowledge the executing block (kernel launched)?</summary>
        public abstract bool IsExecutingBlockAck(byte[] response);
    }

    /// <summary>
    /// E38 (and similar) boot loader. The executing block carries [run-addr(4)][code], written at the load
    /// address; the loader jumps to *(loadAddress), so the image lands at loadAddress+4 and copy blocks must
    /// target loadAddress + offset + 4 to stay contiguous past that 4-byte seam. Blocks are sent highest
    /// offset first so the offset-0 entry block (the executing one) goes last. RequestDownload declares the
    /// total framed length of every block, and the executing block is acked 0x99 (kernel running).
    /// </summary>
    public sealed class E38KernelUploadProtocol : CanKernelUploadProtocol
    {
        // Executing-block header: 0x36 0x80 + 4-byte load + 4-byte run. Subtracted from every block so a
        // small interface's CAN buffer is never exceeded.
        private const int ExecChunkHeaderLength = 10;

        public override CanKernelUpload BuildUpload(Gmlan gmlan, byte[] payload, uint loadAddress, uint runAddress, int maxBlockSize)
        {
            int blockSize = maxBlockSize - ExecChunkHeaderLength;
            if (blockSize < 1)
            {
                blockSize = 1; // defensive; never happens with a real interface
            }

            int chunkCount = payload.Length / blockSize;
            int remainder = payload.Length % blockSize;

            List<CanUploadBlock> blocks = new List<CanUploadBlock>();

            // Tail bytes at the highest offset. (If the whole payload fits one block, offset is 0 and this
            // becomes the executing block below.)
            int offset = chunkCount * blockSize;
            if (remainder > 0)
            {
                blocks.Add(MakeBlock(gmlan, payload, offset, remainder, loadAddress, runAddress));
            }

            // Full blocks, highest offset down to 0; the offset-0 block is the executing one.
            for (int chunkIndex = chunkCount; chunkIndex > 0; chunkIndex--)
            {
                offset = (chunkIndex - 1) * blockSize;
                blocks.Add(MakeBlock(gmlan, payload, offset, blockSize, loadAddress, runAddress));
            }

            // Declare the total framed length of every block, not just the code size (3-byte size field).
            int framedTotal = 0;
            foreach (CanUploadBlock block in blocks)
            {
                framedTotal += block.Message.GetBytes().Length;
            }

            Message requestDownload = gmlan.CreateRequestDownloadRequest(framedTotal, sizeBytes: 3);
            return new CanKernelUpload(requestDownload, blocks);
        }

        public override bool IsExecutingBlockAck(byte[] response)
            => response.Length > 0 && response[0] == Gmlan.ExecChunkAck; // 0x99

        private static CanUploadBlock MakeBlock(Gmlan gmlan, byte[] payload, int offset, int length, uint loadAddress, uint runAddress)
        {
            byte[] code = new byte[length];
            Buffer.BlockCopy(payload, offset, code, 0, length);

            if (offset == 0)
            {
                // Entry block: jumps to runAddress once the whole image is in RAM.
                return new CanUploadBlock(gmlan.CreateExecChunkMessage(code, loadAddress, runAddress), isExecuting: true);
            }

            // Copy block: targets loadAddress + offset + 4 to sit past the 4-byte run-address seam.
            return new CanUploadBlock(gmlan.CreateNonExecChunkMessage(code, loadAddress + (uint)offset + 4), isExecuting: false);
        }
    }

    /// <summary>
    /// P05c (CAN-only 68k) boot loader. The kernel is sent as plain copy blocks (0x36/0x00) placed at their
    /// literal load address - no run-address seam - then a bare execute frame (0x36/0x80 + address, no
    /// run-address and no code) jumps to the load address once the whole image is in RAM. RequestDownload
    /// declares the raw code size with a 16-bit length. Copy blocks are acked 0x76; the boot loader acks
    /// the execute frame with 0x76 as well and then jumps, after which the kernel announces itself with
    /// 0x99 - so either byte confirms the launch.
    /// </summary>
    public sealed class P05cKernelUploadProtocol : CanKernelUploadProtocol
    {
        // Copy-block header: 0x36 0x00 + 4-byte address.
        private const int CopyChunkHeaderLength = 6;

        public override CanKernelUpload BuildUpload(Gmlan gmlan, byte[] payload, uint loadAddress, uint runAddress, int maxBlockSize)
        {
            int blockSize = maxBlockSize - CopyChunkHeaderLength;
            if (blockSize < 1)
            {
                blockSize = 1; // defensive; never happens with a real interface
            }

            List<CanUploadBlock> blocks = new List<CanUploadBlock>();

            // All code as copy blocks, low address first, each placed at its literal load address.
            for (int offset = 0; offset < payload.Length; offset += blockSize)
            {
                int length = Math.Min(blockSize, payload.Length - offset);
                byte[] code = new byte[length];
                Buffer.BlockCopy(payload, offset, code, 0, length);
                blocks.Add(new CanUploadBlock(
                    gmlan.CreateNonExecChunkMessage(code, loadAddress + (uint)offset), isExecuting: false));
            }

            // Bare execute frame: jump to the load address (the kernel entry) after the image is in RAM.
            blocks.Add(new CanUploadBlock(gmlan.CreateExecuteMessage(loadAddress), isExecuting: true));

            // RequestDownload announces the raw code size (16-bit; kernels are well under 64 KiB).
            Message requestDownload = gmlan.CreateRequestDownloadRequest(payload.Length, sizeBytes: 2);
            return new CanKernelUpload(requestDownload, blocks);
        }

        public override bool IsExecutingBlockAck(byte[] response)
            => response.Length > 0 && (response[0] == Gmlan.NonExecChunkAck   // 0x76: boot loader accepted execute
                                    || response[0] == Gmlan.ExecChunkAck);     // 0x99: kernel startup announce
    }
}

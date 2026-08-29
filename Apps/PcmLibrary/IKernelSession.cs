// SPDX-License-Identifier: GPL-3.0-only
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// A kernel running on a PCM, and the few operations that reaching it differs by bus.
    /// <see cref="KernelReader"/> drives one of these, so the read algorithm - block loop, retries,
    /// progress, image sizing - is written once for VPW and CAN.
    /// </summary>
    /// <remarks>
    /// Everything here is either bus-specific mechanics or a tuning value the bus owns. Anything that
    /// is the same reasoning on both buses belongs in the reader, not in this interface.
    /// </remarks>
    public interface IKernelSession
    {
        /// <summary>The command set for this bus, for the operations that are not read-loop specific.</summary>
        IPcmCommands Commands { get; }

        /// <summary>
        /// Everything from bus preparation to a kernel running and answering: on VPW the 4X switch,
        /// the optional loader, and the kernel upload; on CAN the kernel upload alone. Identifying the
        /// flash chip is part of it, because the two buses disagree about whether failing to is fatal.
        /// </summary>
        Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, CancellationToken cancellationToken);

        /// <summary>
        /// The flash chip <see cref="Start"/> identified, or null where it could not be. Null means
        /// the PCM profile's image size stands, since there is no detected size to prefer over it.
        /// </summary>
        FlashChip? FlashChip { get; }

        /// <summary>Largest block this session can ask for in one read. Valid after <see cref="Start"/>.</summary>
        int MaxReadBlockSize { get; }

        /// <summary>How many times to attempt one block before giving up on the read.</summary>
        int MaxBlockAttempts { get; }

        /// <summary>Read one block of PCM memory.</summary>
        Task<Response<byte[]>> ReadMemoryBlock(uint address, int length, CancellationToken cancellationToken);

        /// <summary>
        /// Keep the kernel's bus alive between blocks. The VPW read kernel needs traffic here or it
        /// drops messages; on CAN this does nothing.
        /// </summary>
        Task KeepAlive(CancellationToken cancellationToken);

        /// <summary>Drop anything queued, so a retry after a garbled block starts from a known state.</summary>
        void ResetTransport();

        /// <summary>
        /// Check the image just read, and say whether to trust it. The two buses still check
        /// differently - VPW compares each range's CRC against the PCM, CAN validates the image's own
        /// checksums - so each session reports its own result and logs its own detail.
        /// </summary>
        Task<ResponseStatus> Verify(byte[] image, int imageSize, CancellationToken cancellationToken);
    }
}

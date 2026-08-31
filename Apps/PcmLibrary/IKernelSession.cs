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
        /// <param name="kernelAlreadyRunning">
        /// True when a kernel is already live (the write manager found one before calling), so the
        /// upload is skipped and only the chip is (re)identified. The read path always passes false.
        /// The IAC probe runs only for <see cref="KernelOperation.Read"/>; a write never needs it, and
        /// on a P01/P59 it crashes the kernel.
        /// </param>
        Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, bool kernelAlreadyRunning, CancellationToken cancellationToken);

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

        // ---- Write operations, driven by KernelWriter -------------------------------------------

        /// <summary>
        /// Compare each in-scope range's on-device CRC against the image and record both on the range
        /// (DesiredCrc/ActualCrc), so the caller's write plan can tell which ranges differ. VPW polls
        /// the kernel through <see cref="KernelVerifier"/>; CAN asks for each range's CRC directly.
        /// Also prints the comparison table.
        /// </summary>
        Task<CrcVerificationResult> CompareRanges(
            byte[] image, BlockType relevantBlocks, uint effectiveImageSize, uint baseAddress, CancellationToken cancellationToken);

        /// <summary>Erase the sector that contains this range's base address.</summary>
        Task<bool> EraseRange(MemoryRange range, uint baseAddress, CancellationToken cancellationToken);

        /// <summary>
        /// Write one range in device-sized blocks, reporting progress. Returns Success with the retry
        /// count on the response; a non-Success status aborts the write.
        /// </summary>
        Task<Response<bool>> WriteRange(
            MemoryRange range, byte[] image, uint baseAddress, bool justTestWrite,
            System.DateTime startTime, uint totalSize, uint bytesRemaining, CancellationToken cancellationToken);

        /// <summary>Largest payload this session writes in one block.</summary>
        int MaxWriteBlockSize { get; }

        /// <summary>Called at the start of each erase/write pass, so a session can reset its throughput timer.</summary>
        void BeginWritePass();

        /// <summary>
        /// A recovery aid a bus may run after a failed write - the VPW writer erases calibration to
        /// force the PCM into recovery mode. A no-op where the bus has no such step.
        /// </summary>
        Task AfterFailedWrite(BlockType relevantBlocks, WriteType writeType, CancellationToken cancellationToken);

        /// <summary>Ask the user for diagnostic logs after a hardware-looking failure. A no-op on buses that do not.</summary>
        void RequestDiagnostics(CancellationToken cancellationToken);

        /// <summary>
        /// After the kernel is running and before any erase/write, confirm the operating system on the
        /// PCM matches the file. Returns false to abort. The VPW writer asks the kernel for the OSID
        /// and halts on a mismatch when a partial write requires it; CAN has no equivalent and returns
        /// true.
        /// </summary>
        Task<bool> VerifyOperatingSystem(
            FileValidator validator, bool needToCheckOperatingSystem, WriteType writeType, CancellationToken cancellationToken);
    }
}

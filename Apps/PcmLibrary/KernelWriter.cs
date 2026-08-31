// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// How much of the PCM to erase and rewrite.
    /// </summary>
    public enum WriteType
    {
        None = 0,
        Compare,
        TestWrite,
        Calibration,
        Parameters,
        OsPlusCalibrationPlusBoot,
        Full,
    }

    /// <summary>
    /// Writes (or test-writes/compares) a PCM's flash through a running kernel. The bus-specific
    /// parts - getting the kernel running, the CRC compare, erase, and block write - are behind
    /// <see cref="IKernelSession"/>, so the write algorithm (the compare/erase/write/verify loop, the
    /// write plan, the boot-sector policy, the reporting) is written once for VPW and CAN.
    /// </summary>
    public class KernelWriter
    {
        private readonly IKernelSession session;
        private readonly OSIDInfo pcmInfo;
        private readonly WriteType writeType;
        private readonly ILogger logger;

        // Set from RuntimeSettings at the start of Write and cleared once the forced pass has run, so
        // a retry does not force again.
        private bool forceWriteAllSectorsPending;

        // How much of the chip is in use; set once the chip is identified. Normally the detected chip
        // size, except P10/P11 which carry a larger chip than they use.
        private uint effectiveImageSize;

        // Accumulated message retries across the write, for the end-of-write summary. A field rather
        // than a ref parameter because the erase/write pass runs in an async method.
        private int messageRetryCount;

        public KernelWriter(IKernelSession session, OSIDInfo pcmInfo, WriteType writeType, ILogger logger)
        {
            this.session = session;
            this.pcmInfo = pcmInfo;
            this.writeType = writeType;
            this.logger = logger;
        }

        /// <summary>
        /// Write (or test-write/compare) the image. Assumes the PCM is already unlocked.
        /// </summary>
        /// <param name="kernelAlreadyRunning">
        /// True when a kernel is already live and the upload should be skipped (VPW recovery path).
        /// </param>
        public async Task<bool> Write(
            byte[] image, FileValidator validator, bool needToCheckOperatingSystem, bool kernelAlreadyRunning, CancellationToken cancellationToken)
        {
            if (!await this.session.Start(this.pcmInfo, KernelOperation.Write, kernelAlreadyRunning, cancellationToken))
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // Confirm the running kernel's PCM matches the file before touching flash (VPW only).
            if (!await this.session.VerifyOperatingSystem(validator, needToCheckOperatingSystem, this.writeType, cancellationToken))
            {
                return false;
            }

            BlockType relevantBlocks = RelevantBlocks(this.writeType, this.pcmInfo);

            FlashChip flashChip = this.session.FlashChip
                ?? throw new InvalidOperationException("The flash chip was not identified, so a write cannot proceed.");

            if (!this.TryResolveImageSize(image, flashChip, out this.effectiveImageSize))
            {
                return false;
            }

            uint baseAddress = (uint)this.pcmInfo.ImageBaseAddress;
            this.forceWriteAllSectorsPending = RuntimeSettings.ForceWriteAllSectors;
            this.messageRetryCount = 0;

            bool allRangesMatch = false;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                this.logger.StatusUpdateReset();

                CrcVerificationResult result = await this.session.CompareRanges(
                    image, relevantBlocks, this.effectiveImageSize, baseAddress, cancellationToken);

                if (result == CrcVerificationResult.Cancelled)
                {
                    return false;
                }

                if (result == CrcVerificationResult.Timeout)
                {
                    this.logger.AddUserMessage("PCM stopped responding during write verification. Aborting.");
                    return false;
                }

                if (result == CrcVerificationResult.Verified)
                {
                    allRangesMatch = true;

                    // Don't stop here if the user just wants to test their cable.
                    if (this.writeType == WriteType.TestWrite)
                    {
                        if (attempt == 1)
                        {
                            this.logger.AddUserMessage("Beginning test.");
                        }
                    }
                    else if (!this.forceWriteAllSectorsPending)
                    {
                        this.logger.AddUserMessage("All relevant ranges are identical.");
                        if (attempt > 1)
                        {
                            Utility.ReportRetryCount("Write", this.messageRetryCount, this.pcmInfo.ImageSize, this.logger);
                        }
                        break;
                    }
                }

                // For test writes, report results after the first iteration, then we're done.
                if ((this.writeType == WriteType.TestWrite) && (attempt > 1))
                {
                    this.logger.AddUserMessage("Test write complete.");
                    Utility.ReportRetryCount("Write", this.messageRetryCount, this.pcmInfo.ImageSize, this.logger);
                    return true;
                }

                // Stop now if the user only requested a comparison.
                if (this.writeType == WriteType.Compare)
                {
                    this.logger.AddUserMessage("Note that mismatched Parameter blocks are to be expected.");
                    this.logger.AddUserMessage("Parameter data can change every time the PCM is used.");
                    return true;
                }

                // Preflight policy gate: block before any erase/write if this PCM does not allow
                // boot-sector writes and the plan would write boot.
                if (!WritePlan.BootPolicyAllowsWritePlan(
                        this.writeType, this.pcmInfo.IsSupportedWriteBootSector, relevantBlocks,
                        this.effectiveImageSize, flashChip.MemoryRanges, this.forceWriteAllSectorsPending))
                {
                    foreach (string line in WritePlan.DescribeBootSectorAbort(
                        this.pcmInfo.HardwareType, this.forceWriteAllSectorsPending))
                    {
                        this.logger.AddUserMessage(line);
                    }

                    return false;
                }

                if (WritePlan.ForcedPlanExcludesBoot(
                        this.writeType, this.pcmInfo.IsSupportedWriteBootSector, relevantBlocks,
                        this.effectiveImageSize, flashChip.MemoryRanges, this.forceWriteAllSectorsPending))
                {
                    this.logger.AddUserMessage(WritePlan.DescribeBootExclusion(this.pcmInfo.HardwareType));
                }

                if (!await this.EraseAndWriteRanges(image, flashChip, relevantBlocks, baseAddress, cancellationToken))
                {
                    return false;
                }

                // The forced full-write pass, if any, is now done.
                this.forceWriteAllSectorsPending = false;
            }

            if (allRangesMatch)
            {
                if (this.writeType != WriteType.Compare && this.writeType != WriteType.TestWrite)
                {
                    this.logger.AddUserMessage("Flash successful!");
                }

                return true;
            }

            await this.ReportWriteFailed(relevantBlocks, cancellationToken);
            return false;
        }

        /// <summary>Erase and rewrite every range this pass should process; false stops the write.</summary>
        private async Task<bool> EraseAndWriteRanges(
            byte[] image, FlashChip flashChip, BlockType relevantBlocks, uint baseAddress,
            CancellationToken cancellationToken)
        {
            DateTime startTime = DateTime.Now;
            this.session.BeginWritePass();
            uint totalSize = this.GetTotalSize(flashChip, relevantBlocks);
            uint bytesRemaining = totalSize;

            foreach (MemoryRange range in flashChip.MemoryRanges)
            {
                if (!this.ShouldProcess(range, relevantBlocks))
                {
                    continue;
                }

                this.logger.AddUserMessage(string.Format(
                    "Processing range {0:X6}-{1:X6}", range.Address, range.Address + (range.Size - 1)));

                if (cancellationToken.IsCancellationRequested)
                {
                    this.logger.AddUserMessage("Cancelled before erase.");
                    return false;
                }

                if (this.writeType == WriteType.TestWrite)
                {
                    this.logger.AddUserMessage("Pretending to erase.");
                }
                else if (!await this.session.EraseRange(range, baseAddress, cancellationToken))
                {
                    return false;
                }

                this.logger.AddUserMessage(this.writeType == WriteType.TestWrite ? "Pretending to write..." : "Writing...");

                Response<bool> writeResponse = await this.session.WriteRange(
                    range, image, baseAddress, this.writeType == WriteType.TestWrite,
                    startTime, totalSize, bytesRemaining, cancellationToken);

                if (writeResponse.RetryCount > 0)
                {
                    this.logger.AddUserMessage("Retry count for this block: " + writeResponse.RetryCount);
                    this.messageRetryCount += writeResponse.RetryCount;
                }

                this.logger.StatusUpdateRetryCount(this.messageRetryCount > 0
                    ? this.messageRetryCount + (this.messageRetryCount > 1 ? " Retries" : " Retry")
                    : string.Empty);

                if (writeResponse.Status != ResponseStatus.Success)
                {
                    return false;
                }

                if (writeResponse.Value)
                {
                    bytesRemaining -= range.Size;
                }
            }

            return true;
        }

        /// <summary>Which block types this write covers.</summary>
        private static BlockType RelevantBlocks(WriteType writeType, OSIDInfo pcmInfo)
        {
            switch (writeType)
            {
                case WriteType.Compare:
                    return BlockType.All;

                case WriteType.TestWrite:
                    // Can't write by segment: only a whole-image write is possible, so test all of it.
                    return pcmInfo.IsSupportedWriteBySegment ? BlockType.Calibration : BlockType.All;

                case WriteType.Calibration:
                    return BlockType.Calibration;

                case WriteType.Parameters:
                    return BlockType.Parameter;

                case WriteType.OsPlusCalibrationPlusBoot:
                    return (BlockType)(BlockType.All - BlockType.Parameter);

                case WriteType.Full:
                    return BlockType.All;

                default:
                    throw new InvalidDataException("Unsuppported operation type: " + writeType.ToString());
            }
        }

        /// <summary>
        /// Decide how much of the chip is in use and confirm the file fits it. A P10/P11 carries a
        /// 1MiB chip but uses only the lower 512KiB, so a 512KiB image on a 1MiB chip is allowed and
        /// the PCM-type size stands; every other PCM must match the detected chip exactly. This is the
        /// shared VPW/CAN rule - the P10/P11 branch simply never fires for a CAN PCM.
        /// </summary>
        private bool TryResolveImageSize(byte[] image, FlashChip flashChip, out uint size)
        {
            bool usesLessThanChip = this.pcmInfo.HardwareType == PcmType.P10 || this.pcmInfo.HardwareType == PcmType.P11;

            if (usesLessThanChip && image.Length == 512 * 1024 && flashChip.Size == 1024 * 1024)
            {
                this.logger.AddUserMessage(string.Format(
                    "File size {0:n0} for flash chip size {1:n0}. Allowable for {2}.",
                    image.Length, flashChip.Size, this.pcmInfo.HardwareType));
            }
            else if (image.Length != flashChip.Size)
            {
                this.logger.AddUserMessage(string.Format(
                    "File size {0:n0} does not match flash chip size {1:n0}. This image is not compatible with this PCM.",
                    image.Length, flashChip.Size));
                size = 0;
                return false;
            }

            size = usesLessThanChip ? (uint)this.pcmInfo.ImageSize : flashChip.Size;
            return true;
        }

        private uint GetTotalSize(FlashChip chip, BlockType relevantBlocks)
        {
            uint result = 0;
            foreach (MemoryRange range in chip.MemoryRanges)
            {
                if (this.ShouldProcess(range, relevantBlocks))
                {
                    result += range.Size;
                }
            }

            return result;
        }

        private bool ShouldProcess(MemoryRange range, BlockType relevantBlocks)
        {
            return WritePlan.ShouldProcessRange(
                range, relevantBlocks, this.writeType, this.effectiveImageSize,
                this.forceWriteAllSectorsPending, this.pcmInfo.IsSupportedWriteBootSector);
        }

        private async Task ReportWriteFailed(BlockType relevantBlocks, CancellationToken cancellationToken)
        {
            this.logger.AddUserMessage("===============================================");
            this.logger.AddUserMessage("THE CHANGES WERE -NOT- WRITTEN SUCCESSFULLY");
            this.logger.AddUserMessage("===============================================");

            // Bus-specific recovery aid (VPW erases calibration to force recovery mode).
            await this.session.AfterFailedWrite(relevantBlocks, this.writeType, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                this.logger.AddUserMessage("");
                this.logger.AddUserMessage("The operation was cancelled.");
                this.logger.AddUserMessage("This PCM is probably not usable in its current state.");
                this.logger.AddUserMessage("");
            }
            else
            {
                this.logger.AddUserMessage("This may indicate a hardware problem on the PCM.");
                this.logger.AddUserMessage("We tried, and re-tried, and it still didn't work.");
                this.logger.AddUserMessage("");
                this.logger.AddUserMessage("Please start a new thread at pcmhacking.net, and");
                this.logger.AddUserMessage("include the contents of the debug tab.");
                this.session.RequestDiagnostics(cancellationToken);
            }
        }
    }
}

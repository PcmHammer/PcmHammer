// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Writes (or test-writes) the flash of a GM CAN PCM via an uploaded kernel. Uploads the write
    /// kernel, then loops (up to five times) comparing each range's on-device CRC32 against the image,
    /// erasing and rewriting only the ranges that differ, and re-verifying. A test write exercises the
    /// whole transaction but the kernel never erases or programs, so it is safe for proving a setup.
    /// </summary>
    public class CanKernelWriter
    {
        private readonly Vehicle vehicle;
        private readonly CanCommands commands;
        private readonly OSIDInfo pcmInfo;
        private readonly WriteType writeType;
        private readonly ILogger logger;

        // How much of the flash chip we will verify and (re)write. Set once the chip is identified.
        private UInt32 effectiveImageSize;

        // True while a "Force write all flash sectors" pass is owed; cleared after it runs once,
        // so later retries rewrite only what still differs.
        private bool forceWriteAllSectorsPending;

        // Reported throughput is the block round-trip rate: bytes moved / time spent in block transfers
        // only, so erase, verify, and idle time don't distort the figure.
        private readonly System.Diagnostics.Stopwatch blockTransferTimer = new System.Diagnostics.Stopwatch();
        private long blockTransferBytes;

        public CanKernelWriter(Vehicle vehicle, CanCommands commands, OSIDInfo pcmInfo, WriteType writeType, ILogger logger)
        {
            this.vehicle = vehicle;
            this.commands = commands;
            this.pcmInfo = pcmInfo;
            this.writeType = writeType;
            this.logger = logger;
        }

        /// <summary>
        /// Upload the write kernel and write (or test-write) the changed flash ranges. PCM must already
        /// be unlocked.
        /// </summary>
        public async Task<bool> Write(byte[] image, FileValidator validator, CancellationToken cancellationToken)
        {
            bool success = false;

            try
            {
                this.vehicle.ClearDeviceMessageQueue();

                // Compare (verify) only needs the CRC, which lives in the read kernel; an actual
                // write/test-write needs the write kernel. Most PCMs define one kernel for both.
                KernelOperation kernelOp = this.writeType == WriteType.Compare
                    ? KernelOperation.Read
                    : KernelOperation.Write;
                string kernelFile = this.pcmInfo.GetKernelFileName(kernelOp);
                Response<byte[]> kernel = await this.vehicle.LoadKernelFromFile(kernelFile);
                if (kernel.Status != ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Failed to load CAN write kernel: " + kernelFile);
                    return false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                this.logger.StatusUpdateActivity("Uploading kernel to PCM...");
                if (!await this.commands.UploadKernel(this.pcmInfo, kernel.Value, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    this.logger.AddUserMessage("Failed to upload kernel to PCM.");
                    return false;
                }

                this.logger.AddUserMessage("Kernel uploaded to PCM successfully.");

                // The kernel needs a moment after the start ack before it answers queries.
                await Task.Delay(300, cancellationToken);

                Response<uint> version = await this.commands.GetKernelVersion(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (version.Status == ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Kernel version: " + CanCommands.FormatKernelVersion(version.Value));
                }
                else
                {
                    this.logger.AddUserMessage("Kernel did not report a version (" + version.Status + "); continuing.");
                }

                success = await this.Write(cancellationToken, image);

                return success;
            }
            catch (OperationCanceledException)
            {
                // Clean user-requested abort; the caller reports it, so don't imply a fault here.
                logger.AddDebugMessage("Write cancelled by user.");
                return false;
            }
            catch (Exception exception)
            {
                // On a requested stop, treat whatever surfaced as part of that clean abort.
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.AddDebugMessage("Write cancelled by user: " + exception.Message);
                    return false;
                }

                if (!success)
                {
                    switch (this.writeType)
                    {
                        case WriteType.None:
                        case WriteType.Compare:
                        case WriteType.TestWrite:
                            logger.AddUserMessage("Something has gone wrong. Please report this error.");
                            logger.AddUserMessage("Errors during comparisons or test writes indicate a");
                            logger.AddUserMessage("problem with the PCM, interface, cable, or app. Don't");
                            logger.AddUserMessage("try to do any actual writing until you are certain that");
                            logger.AddUserMessage("the underlying problem has been completely corrected.");
                            break;

                        default:
                            logger.AddUserMessage("Something went wrong. " + exception.Message);
                            logger.AddUserMessage("Do not power off the PCM! Do not exit this program!");
                            logger.AddUserMessage("Try flashing again. If errors continue, seek help online.");
                            break;
                    }

                    logger.AddUserMessage(string.Empty);
                    logger.AddDebugMessage(exception.ToString());
                }

                return success;
            }
            finally
            {
                // Always return the PCM to normal, on every exit path. Cleanup is best-effort and
                // swallows its own errors, so it is safe to run even while a cancellation unwinds.
                await this.Cleanup();
                logger.StatusUpdateReset();
            }
        }

        /// <summary>Compare, erase, and rewrite the relevant flash ranges.</summary>
        private async Task<bool> Write(CancellationToken cancellationToken, byte[] image)
        {
            BlockType relevantBlocks;
            switch (this.writeType)
            {
                case WriteType.Compare:
                    relevantBlocks = BlockType.All;
                    break;

                case WriteType.TestWrite:
                    // Can't write by segment: only a whole-image write is possible, so test all of it.
                    relevantBlocks = this.pcmInfo.IsSupportedWriteBySegment
                        ? BlockType.Calibration
                        : BlockType.All;
                    break;

                case WriteType.Calibration:
                    relevantBlocks = BlockType.Calibration;
                    break;

                case WriteType.Parameters:
                    relevantBlocks = BlockType.Parameter;
                    break;

                case WriteType.OsPlusCalibrationPlusBoot:
                    relevantBlocks = (BlockType)(BlockType.All - BlockType.Parameter);
                    break;

                case WriteType.Full:
                    relevantBlocks = BlockType.All;
                    break;

                default:
                    throw new InvalidDataException("Unsuppported operation type: " + this.writeType.ToString());
            }

            // Which flash chip?
            Response<uint> chipIdResponse = await this.commands.GetFlashId(cancellationToken);
            if (chipIdResponse.Status != ResponseStatus.Success)
            {
                logger.AddUserMessage("Flash chip ID query failed (" + chipIdResponse.Status + ").");
                return false;
            }

            FlashChip flashChip = FlashChip.Create(chipIdResponse.Value, this.logger);
            logger.AddUserMessage("Flash chip: " + flashChip.ToString());

            if (image.Length != flashChip.Size)
            {
                logger.AddUserMessage(string.Format("File size {0:n0} does not match flash chip size {1:n0}. This image is not compatible with this PCM.", image.Length, flashChip.Size));
                return false;
            }

            this.effectiveImageSize = flashChip.Size;
            uint baseAddress = (uint)this.pcmInfo.ImageBaseAddress;

            this.forceWriteAllSectorsPending = RuntimeSettings.ForceWriteAllSectors;

            bool allRangesMatch = false;
            int messageRetryCount = 0;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                logger.StatusUpdateReset();

                RangeCompareResult verificationResult = await this.CompareRanges(
                    image,
                    flashChip,
                    relevantBlocks,
                    baseAddress,
                    cancellationToken);

                if (verificationResult == RangeCompareResult.Cancelled)
                {
                    return false;
                }

                if (verificationResult == RangeCompareResult.Timeout)
                {
                    logger.AddUserMessage("PCM stopped responding during write verification. Aborting.");
                    return false;
                }

                if (verificationResult == RangeCompareResult.Verified)
                {
                    allRangesMatch = true;

                    // Don't stop here if the user just wants to test their cable.
                    if (this.writeType == WriteType.TestWrite)
                    {
                        if (attempt == 1)
                        {
                            logger.AddUserMessage("Beginning test.");
                        }
                    }
                    else if (!this.forceWriteAllSectorsPending)
                    {
                        logger.AddUserMessage("All relevant ranges are identical.");
                        if (attempt > 1)
                        {
                            Utility.ReportRetryCount("Write", messageRetryCount, pcmInfo.ImageSize, this.logger);
                        }
                        break;
                    }
                }

                // For test writes, report results after the first iteration, then we're done.
                if ((this.writeType == WriteType.TestWrite) && (attempt > 1))
                {
                    logger.AddUserMessage("Test write complete.");
                    Utility.ReportRetryCount("Write", messageRetryCount, pcmInfo.ImageSize, this.logger);
                    return true;
                }

                // Stop now if the user only requested a comparison.
                if (this.writeType == WriteType.Compare)
                {
                    logger.AddUserMessage("Note that mismatched Parameter blocks are to be expected.");
                    logger.AddUserMessage("Parameter data can change every time the PCM is used.");
                    return true;
                }

                // Preflight policy gate: block before any erase/write if this PCM does not allow
                // boot-sector writes and the plan would write boot.
                if (!this.IsWritePlanAllowedByPcmInfo(flashChip, relevantBlocks))
                {
                    this.ReportBootSectorAbort();
                    return false;
                }

                this.ReportBootExcludedFromForcedPlan(flashChip, relevantBlocks);

                // Erase and rewrite the required memory ranges.
                DateTime startTime = DateTime.Now;
                this.blockTransferTimer.Reset();
                this.blockTransferBytes = 0;
                UInt32 totalSize = this.GetTotalSize(flashChip, relevantBlocks);
                UInt32 bytesRemaining = totalSize;
                foreach (MemoryRange range in flashChip.MemoryRanges)
                {
                    if (!this.ShouldProcess(range, relevantBlocks))
                    {
                        continue;
                    }

                    logger.AddUserMessage(
                        string.Format(
                            "Processing range {0:X6}-{1:X6}",
                            range.Address,
                            range.Address + (range.Size - 1)));

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger.AddUserMessage("Cancelled before erase.");
                        return false;
                    }

                    if (this.writeType == WriteType.TestWrite)
                    {
                        logger.AddUserMessage("Pretending to erase.");
                    }
                    else
                    {
                        if (!await this.EraseRange(range, baseAddress, cancellationToken))
                        {
                            return false;
                        }
                    }

                    if (this.writeType == WriteType.TestWrite)
                    {
                        logger.AddUserMessage("Pretending to write...");
                    }
                    else
                    {
                        logger.AddUserMessage("Writing...");
                    }

                    Response<bool> writeResponse = await this.WriteRange(
                        range,
                        image,
                        baseAddress,
                        this.writeType == WriteType.TestWrite,
                        startTime,
                        totalSize,
                        bytesRemaining,
                        cancellationToken);

                    if (writeResponse.RetryCount > 0)
                    {
                        logger.AddUserMessage("Retry count for this block: " + writeResponse.RetryCount);
                        messageRetryCount += writeResponse.RetryCount;
                    }

                    logger.StatusUpdateRetryCount((messageRetryCount > 0) ? messageRetryCount.ToString() + ((messageRetryCount > 1) ? " Retries" : " Retry") : string.Empty);

                    if (writeResponse.Status != ResponseStatus.Success)
                    {
                        return false;
                    }

                    if (writeResponse.Value)
                    {
                        bytesRemaining -= range.Size;
                    }
                }

                // The forced full-write pass, if any, is now done.
                this.forceWriteAllSectorsPending = false;
            }

            if (allRangesMatch)
            {
                if (this.writeType != WriteType.Compare && this.writeType != WriteType.TestWrite)
                {
                    logger.AddUserMessage("Flash successful!");
                }
                return true;
            }

            // During a test write, we will return from the middle of the loop above.
            // So if we made it here, a real write has failed.
            logger.AddUserMessage("===============================================");
            logger.AddUserMessage("THE CHANGES WERE -NOT- WRITTEN SUCCESSFULLY");
            logger.AddUserMessage("===============================================");

            if (cancellationToken.IsCancellationRequested)
            {
                logger.AddUserMessage("");
                logger.AddUserMessage("The operation was cancelled.");
                logger.AddUserMessage("This PCM is probably not usable in its current state.");
                logger.AddUserMessage("");
            }
            else
            {
                logger.AddUserMessage("This may indicate a hardware problem on the PCM.");
                logger.AddUserMessage("We tried, and re-tried, and it still didn't work.");
                logger.AddUserMessage("");
                logger.AddUserMessage("Please start a new thread at pcmhacking.net, and");
                logger.AddUserMessage("include the contents of the debug tab.");
            }

            return false;
        }

        /// <summary>
        /// Compare each relevant range's on-device CRC32 against the image's, recording both on the
        /// range. The desired CRC is computed locally the same way the kernel computes the actual one.
        /// </summary>
        private async Task<RangeCompareResult> CompareRanges(
            byte[] image,
            FlashChip flashChip,
            BlockType relevantBlocks,
            uint baseAddress,
            CancellationToken cancellationToken)
        {
            bool allMatch = true;

            // One fixed-width, space-padded row per range (no tabs) so the table aligns identically in
            // the log view and when copied into a text file. Ranges not in this operation, or past the
            // image, show "not needed". Purpose is the range's own block type, taken from the flash-chip
            // map (not hard-coded per PCM).
            const string formatString = "{0:X6}-{1:X6}  {2,-10:X8}  {3,-10:X8}  {4,-9}  {5}";
            this.logger.AddUserMessage("Calculating CRCs from file.");
            this.logger.AddUserMessage("Requesting CRCs from PCM.");
            this.logger.AddUserMessage(string.Format("{0,-13}  {1,-10}  {2,-10}  {3,-9}  {4}", "Range", "File CRC", "PCM CRC", "Verdict", "Purpose"));

            foreach (MemoryRange range in flashChip.MemoryRanges)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return RangeCompareResult.Cancelled;
                }

                string rangeType = range.Type.ToString();

                if (((range.Type & relevantBlocks) == 0) || (range.Address >= this.effectiveImageSize))
                {
                    this.logger.AddUserMessage(string.Format(
                        formatString, range.Address, range.Address + (range.Size - 1), "not needed", "not needed", "n/a", rangeType));
                    continue;
                }

                range.DesiredCrc = Gmlan.ComputeCrc32(image, (int)range.Address, (int)range.Size);

                Response<uint> actual = await this.commands.GetRangeCrc(baseAddress + range.Address, range.Size, cancellationToken);
                if (actual.Status == ResponseStatus.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    // A cancel during the CRC query is clean, not the PCM going unresponsive.
                    return RangeCompareResult.Cancelled;
                }
                if (actual.Status != ResponseStatus.Success)
                {
                    return RangeCompareResult.Timeout;
                }
                range.ActualCrc = actual.Value;

                bool match = range.DesiredCrc == range.ActualCrc;
                this.logger.AddUserMessage(string.Format(
                    formatString,
                    range.Address,
                    range.Address + (range.Size - 1),
                    range.DesiredCrc,
                    range.ActualCrc,
                    match ? "Same" : "Different",
                    rangeType));

                if (!match)
                {
                    allMatch = false;
                }
            }

            return allMatch ? RangeCompareResult.Verified : RangeCompareResult.Different;
        }

        private UInt32 GetTotalSize(FlashChip chip, BlockType relevantBlocks)
        {
            UInt32 result = 0;
            foreach (MemoryRange range in chip.MemoryRanges)
            {
                if (this.ShouldProcess(range, relevantBlocks))
                {
                    result += range.Size;
                }
            }

            return result;
        }

        /// <summary>
        /// Check the write plan against PCM policy before any erase/write occurs. Mirrors the VPW
        /// writer: a PCM that does not support boot-sector writes must not have boot erased/written.
        /// </summary>
        private bool IsWritePlanAllowedByPcmInfo(FlashChip flashChip, BlockType relevantBlocks)
        {
            // Pass the SAME force flag the write loop (ShouldProcess) uses. Without it a forced full
            // write passes this gate by CRC and then writes boot anyway - a hard brick on a PCM whose
            // boot sector cannot be rewritten.
            return BootPolicyAllowsWritePlan(
                this.writeType,
                this.pcmInfo.IsSupportedWriteBootSector,
                relevantBlocks,
                this.effectiveImageSize,
                flashChip.MemoryRanges,
                this.forceWriteAllSectorsPending);
        }

        /// <summary>
        /// Pure boot-sector write policy. Returns false only when a destructive plan on a PCM that
        /// cannot write its boot sector would erase/write a boot range whose content differs.
        /// </summary>
        /// <summary>
        /// Whether the PCM's boot-sector policy permits this write plan. Delegates to the shared
        /// <see cref="WritePlan"/>; see there for why the force flag must be passed through.
        /// </summary>
        public static bool BootPolicyAllowsWritePlan(
            WriteType writeType,
            bool supportsBootSectorWrite,
            BlockType relevantBlocks,
            UInt32 effectiveImageSize,
            IEnumerable<MemoryRange> memoryRanges,
            bool forceAllSectors = false)
        {
            return WritePlan.BootPolicyAllowsWritePlan(
                writeType, supportsBootSectorWrite, relevantBlocks, effectiveImageSize,
                memoryRanges, forceAllSectors);
        }

        /// <summary>Report why a write was refused by the boot-sector policy.</summary>
        private void ReportBootSectorAbort()
        {
            foreach (string line in WritePlan.DescribeBootSectorAbort(
                this.pcmInfo.HardwareType, this.forceWriteAllSectorsPending))
            {
                logger.AddUserMessage(line);
            }
        }

        /// <summary>Tell the user when a forced pass is proceeding with the boot sector left out.</summary>
        private void ReportBootExcludedFromForcedPlan(FlashChip flashChip, BlockType relevantBlocks)
        {
            if (WritePlan.ForcedPlanExcludesBoot(
                    this.writeType,
                    this.pcmInfo.IsSupportedWriteBootSector,
                    relevantBlocks,
                    this.effectiveImageSize,
                    flashChip.MemoryRanges,
                    this.forceWriteAllSectorsPending))
            {
                logger.AddUserMessage(WritePlan.DescribeBootExclusion(this.pcmInfo.HardwareType));
            }
        }

        private bool ShouldProcess(MemoryRange range, BlockType relevantBlocks)
        {
            // One shared rule for "will this range be written", used by the boot-sector gate too.
            return WritePlan.ShouldProcessRange(
                range, relevantBlocks, this.writeType, this.effectiveImageSize,
                this.forceWriteAllSectorsPending, this.pcmInfo.IsSupportedWriteBootSector);
        }

        /// <summary>
        /// Whether this range will be erased/written. Delegates to the shared <see cref="WritePlan"/>
        /// so the boot-sector gate and the write loop can never disagree.
        /// </summary>
        /// <remarks>
        /// <paramref name="supportsBootSectorWrite"/> has no default on purpose: it changes the answer
        /// for a forced boot range, and a silently-defaulted capability flag is exactly how the gate
        /// and the loop drifted apart before.
        /// </remarks>
        public static bool ShouldProcessRange(
            MemoryRange range,
            BlockType relevantBlocks,
            WriteType writeType,
            UInt32 effectiveImageSize,
            bool forceAllSectors,
            bool supportsBootSectorWrite)
        {
            return WritePlan.ShouldProcessRange(
                range, relevantBlocks, writeType, effectiveImageSize, forceAllSectors, supportsBootSectorWrite);
        }

        /// <summary>
        /// Erase one flash sector. Each FlashChip MemoryRange is exactly one hardware erase sector, so
        /// the kernel erases the sector containing the range's base address.
        /// </summary>
        private async Task<bool> EraseRange(MemoryRange range, uint baseAddress, CancellationToken cancellationToken)
        {
            logger.AddUserMessage("Erasing.");

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            Response<byte> eraseResponse = await this.commands.EraseFlashSector(baseAddress + range.Address, cancellationToken);
            if (eraseResponse.Status != ResponseStatus.Success)
            {
                logger.AddUserMessage(string.Format("Unable to erase flash sector at 0x{0:X6}: {1}", range.Address, eraseResponse.Status));
                return false;
            }

            if (eraseResponse.Value != 0x00)
            {
                logger.AddUserMessage(string.Format("Unable to erase flash sector at 0x{0:X6}. Code: {1:X2}", range.Address, eraseResponse.Value));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write one memory range in device-sized blocks, accumulating the per-block retry count for
        /// the end-of-write summary.
        /// </summary>
        private async Task<Response<bool>> WriteRange(
            MemoryRange range,
            byte[] image,
            uint baseAddress,
            bool justTestWrite,
            DateTime startTime,
            UInt32 totalSize,
            UInt32 bytesRemaining,
            CancellationToken cancellationToken)
        {
            int retryCount = 0;
            int devicePayloadSize = this.commands.MaxFlashWriteBlockSize;
            for (int index = 0; index < range.Size; index += devicePayloadSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, false, retryCount);
                }

                int imageOffset = (int)(range.Address + index);
                uint flashAddress = baseAddress + (uint)imageOffset;
                int thisPayloadSize = Math.Min(devicePayloadSize, (int)range.Size - index);

                logger.AddDebugMessage(string.Format(
                    "Sending payload with offset 0x{0:X6}, length 0x{1:X4}.", imageOffset, thisPayloadSize));

                TimeSpan elapsed = DateTime.Now - startTime;
                UInt32 totalWritten = totalSize - bytesRemaining;
                UInt32 bytesPerSecond = elapsed.TotalSeconds > 0 ? (UInt32)(totalWritten / elapsed.TotalSeconds) : 0;
                string timeRemaining = string.Empty;
                if (bytesPerSecond > 0)
                {
                    UInt32 secondsRemaining = bytesRemaining / bytesPerSecond;
                    timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString("mm\\:ss");
                }

                // Throughput is the block round-trip rate (see blockTransferTimer), not wall-clock time.
                double transferSeconds = this.blockTransferTimer.Elapsed.TotalSeconds;
                double blockBytesPerSecond = transferSeconds > 0 ? this.blockTransferBytes / transferSeconds : 0;

                logger.StatusUpdateActivity($"Writing {thisPayloadSize} bytes to 0x{flashAddress:X6}");
                logger.StatusUpdatePercentDone((totalSize > 0 && totalWritten * 100 / totalSize > 0) ? $"{totalWritten * 100 / totalSize}%" : string.Empty);
                logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
                logger.StatusUpdateKbps((blockBytesPerSecond > 0) ? $"{blockBytesPerSecond * 8.00 / 1000.00:0.00} Kbps" : string.Empty);
                if (totalSize > 0)
                {
                    logger.StatusUpdateProgressBar((double)(totalWritten + (uint)thisPayloadSize) / totalSize, true);
                }

                // WriteFlashBlock owns its retry loop. Time only the block round trip.
                this.blockTransferTimer.Start();
                Response<bool> response = await this.commands.WriteFlashBlock(image, imageOffset, thisPayloadSize, flashAddress, justTestWrite, cancellationToken);
                this.blockTransferTimer.Stop();
                if (response.Status != ResponseStatus.Success)
                {
                    return Response.Create(ResponseStatus.Error, false, retryCount + response.RetryCount);
                }

                this.blockTransferBytes += thisPayloadSize;
                bytesRemaining -= (uint)thisPayloadSize;
                retryCount += response.RetryCount;
            }

            return Response.Create(ResponseStatus.Success, true, retryCount);
        }

        /// <summary>
        /// Return the PCM to its stock OS and clear the codes the kernel session provokes. Uses
        /// CancellationToken.None so cleanup completes after a cancel. Never throws.
        /// </summary>
        private async Task Cleanup()
        {
            try
            {
                this.logger.AddDebugMessage("Returning PCM to normal mode.");
                await this.commands.Reboot(CancellationToken.None);
                await this.commands.ClearDiagnosticCodes(CancellationToken.None);
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Cleanup after write failed: " + exception.Message);
            }
        }

        private enum RangeCompareResult
        {
            Verified,
            Different,
            Timeout,
            Cancelled,
        }
    }
}

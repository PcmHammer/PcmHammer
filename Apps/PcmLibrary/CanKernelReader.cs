// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Reads the full flash of a GM CAN PCM via an uploaded kernel. Assumes the PCM is unlocked;
    /// uploads and runs the read kernel, reads the image one block (0x400 bytes) at a time, validates
    /// it, and reboots back to the stock OS.
    /// </summary>
    public class CanKernelReader
    {
        private readonly Vehicle vehicle;
        private readonly CanCommands commands;
        private readonly OSIDInfo pcmInfo;
        private readonly ILogger logger;

        // Reported throughput is the block round-trip rate: bytes moved / time spent in block transfers
        // only, so re-sync delays and idle time don't distort the figure.
        private readonly System.Diagnostics.Stopwatch blockTransferTimer = new System.Diagnostics.Stopwatch();
        private long blockTransferBytes;

        public CanKernelReader(Vehicle vehicle, CanCommands commands, OSIDInfo pcmInfo, ILogger logger)
        {
            this.vehicle = vehicle;
            this.commands = commands;
            this.pcmInfo = pcmInfo;
            this.logger = logger;
        }

        /// <summary>
        /// Upload the read kernel and read the full flash image. Assumes the PCM is already unlocked.
        /// </summary>
        public async Task<Response<Stream>> ReadContents(CancellationToken cancellationToken, IProgress<ProgressUpdate>? progress = null)
        {
            try
            {
                this.vehicle.ClearDeviceMessageQueue();

                Response<byte[]> kernel = await this.vehicle.LoadKernelFromFile(this.pcmInfo.KernelFileName);
                if (kernel.Status != ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Failed to load CAN read kernel: " + this.pcmInfo.KernelFileName);
                    return Response.Create(kernel.Status, (Stream)null!);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                }

                this.logger.StatusUpdateActivity("Uploading kernel to PCM...");
                if (!await this.commands.UploadKernel(this.pcmInfo, kernel.Value, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }
                    this.logger.AddUserMessage("Failed to upload kernel to PCM.");
                    return Response.Create(ResponseStatus.Error, (Stream)null!);
                }

                this.logger.AddUserMessage("Kernel uploaded to PCM successfully.");

                // The kernel needs a moment after the start ack before it answers queries/reads.
                await Task.Delay(300, cancellationToken);

                uint baseAddress = (uint)this.pcmInfo.ImageBaseAddress;
                int imageSize = this.pcmInfo.ImageSize;

                // Confirm the kernel is alive and identify the flash chip.
                Response<uint> version = await this.commands.GetKernelVersion(cancellationToken);
                if (version.Status == ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Kernel version: " + CanCommands.FormatKernelVersion(version.Value));
                }
                else
                {
                    this.logger.AddUserMessage("Kernel did not report a version (" + version.Status + "); continuing.");
                }

                Response<uint> chipId = await this.commands.GetFlashId(cancellationToken);
                if (chipId.Status == ResponseStatus.Success)
                {
                    try
                    {
                        FlashChip flashChip = FlashChip.Create(chipId.Value, this.logger);
                        this.logger.AddUserMessage("Flash chip: " + flashChip.ToString());

                        // Trust the detected chip's size over the profile size.
                        if (flashChip.Size > 0 && (int)flashChip.Size != imageSize)
                        {
                            this.logger.AddUserMessage(string.Format(
                                "Profile image size is {0}KiB but the detected flash chip is {1}KiB. Reading the full chip.",
                                imageSize / 1024, flashChip.Size / 1024));
                            imageSize = (int)flashChip.Size;
                        }
                    }
                    catch (Exception)
                    {
                        this.logger.AddUserMessage(string.Format("Flash chip ID {0:X8} is not in the known-chip table.", chipId.Value));
                    }
                }
                else
                {
                    this.logger.AddUserMessage("Flash chip ID query failed (" + chipId.Status + "); continuing.");
                }

                this.logger.AddUserMessage("Reading " + (imageSize / 1024) + " KiB...");

                int blockSize = Gmlan.KernelBlockSize;
                byte[] image = new byte[imageSize];
                DateTime startTime = DateTime.Now;
                this.blockTransferTimer.Reset();
                this.blockTransferBytes = 0;

                for (int offset = 0; offset < imageSize; offset += blockSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }

                    uint address = baseAddress + (uint)offset;
                    int thisBlock = Math.Min(blockSize, imageSize - offset);

                    // Retry a failed block: a dropped/garbled frame corrupts one block's reassembly;
                    // re-syncing and re-requesting is cheap and the kernel re-reads idempotently.
                    const int maxBlockAttempts = 4;
                    Response<byte[]> blockResponse = Response.Create(ResponseStatus.Error, Array.Empty<byte>());
                    for (int attempt = 1; attempt <= maxBlockAttempts; attempt++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                        }

                        // Time only the block round trip, not the re-sync delay between failed attempts.
                        this.blockTransferTimer.Start();
                        try
                        {
                            blockResponse = await this.commands.ReadMemoryBlock(address, cancellationToken);
                        }
                        catch (Exception blockEx)
                        {
                            blockResponse = Response.Create(ResponseStatus.Error, Array.Empty<byte>());
                            this.logger.AddDebugMessage(string.Format(
                                "Block 0x{0:X6} attempt {1}/{2} threw: {3}", address, attempt, maxBlockAttempts, blockEx.Message));
                        }
                        finally
                        {
                            this.blockTransferTimer.Stop();
                        }

                        if (blockResponse.Status == ResponseStatus.Success)
                        {
                            break;
                        }

                        if (attempt < maxBlockAttempts)
                        {
                            this.logger.AddUserMessage(string.Format(
                                "Block 0x{0:X6} did not transfer (attempt {1}/{2}, {3}); re-syncing and retrying.",
                                address, attempt, maxBlockAttempts, blockResponse.Status));
                            try { this.vehicle.ClearDeviceMessageQueue(); } catch { /* best effort */ }
                            await Task.Delay(150, cancellationToken);
                        }
                    }

                    if (blockResponse.Status != ResponseStatus.Success)
                    {
                        this.logger.AddUserMessage(string.Format(
                            "Unable to read block at 0x{0:X6}: {1}", address, blockResponse.Status));
                        return Response.Create(blockResponse.Status, (Stream)null!);
                    }

                    Buffer.BlockCopy(blockResponse.Value, 0, image, offset, thisBlock);
                    this.blockTransferBytes += thisBlock;
                    ReportProgress(progress, offset + thisBlock, imageSize, address, startTime);
                }

                this.logger.AddUserMessage("Read complete.");
                this.logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(startTime));

                // Per-segment Sum and CVN. A bad sum means a corrupt image; a bad CVN is only a warning.
                FileValidator.SumCvnVerdict verdict = new FileValidator(image, this.logger).ValidateSumAndCvn();

                // A bad sum makes this a bad read (caller saves it as *_badread); a CVN warning is non-fatal.
                ResponseStatus status = verdict == FileValidator.SumCvnVerdict.SumError
                    ? ResponseStatus.Unverified
                    : ResponseStatus.Success;

                return Response.Create(status, (Stream)new MemoryStream(image));
            }
            catch (Exception exception)
            {
                this.logger.AddUserMessage("Something went wrong. " + exception.Message);
                this.logger.AddDebugMessage(exception.ToString());
                return Response.Create(ResponseStatus.Error, (Stream)null!);
            }
            finally
            {
                // Always return the PCM to its stock OS and clear the codes the kernel session provokes,
                // even on cancel (CancellationToken.None), so the kernel is never left running.
                try
                {
                    this.logger.AddDebugMessage("Returning PCM to normal mode.");
                    await this.commands.Reboot(CancellationToken.None);
                    await this.commands.ClearDiagnosticCodes(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    this.logger.AddDebugMessage("Cleanup after read failed: " + exception.Message);
                }

                this.logger.StatusUpdateReset();
            }
        }

        private void ReportProgress(IProgress<ProgressUpdate>? progress, int bytesRead, int imageSize, uint address, DateTime startTime)
        {
            double percent = imageSize > 0 ? (bytesRead * 100.0) / imageSize : 100.0;
            double seconds = Math.Max(DateTime.Now.Subtract(startTime).TotalSeconds, 0.001);
            double wallRate = bytesRead / seconds;
            double secondsRemaining = wallRate > 0 ? (imageSize - bytesRead) / wallRate : 0;
            string timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString(@"mm\:ss");

            // Throughput is the block round-trip rate (see blockTransferTimer), not wall-clock time.
            double transferSeconds = this.blockTransferTimer.Elapsed.TotalSeconds;
            double rate = transferSeconds > 0 ? this.blockTransferBytes / transferSeconds : 0;

            this.logger.StatusUpdateActivity($"Reading 0x{address:X6}");
            this.logger.StatusUpdatePercentDone($"{percent:0}%");
            this.logger.StatusUpdateProgressBar(percent / 100.0, true);
            this.logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
            this.logger.StatusUpdateKbps(rate > 0 ? $"{rate * 8.0 / 1000.0:0.00} Kbps" : string.Empty);

            progress?.Report(new ProgressUpdate
            {
                PayloadLength = bytesRead,
                TotalLength = imageSize,
                Address = $"0x{address:X6}",
                Percentage = percent,
                Rate = rate,
                TimeRemaining = timeRemaining,
            });
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Reads a PCM's whole flash through a running kernel. The bus-specific parts - getting the kernel
    /// running, moving one block, keeping the bus alive, checking the result - are behind
    /// <see cref="IKernelSession"/>, so this algorithm is written once for VPW and CAN.
    /// </summary>
    public class KernelReader
    {
        private readonly IKernelSession session;
        private readonly OSIDInfo pcmInfo;
        private readonly ILogger logger;

        // Reported throughput is the block round-trip rate: bytes moved / time spent in block
        // transfers only, so re-sync delays and idle time don't distort the figure.
        private readonly System.Diagnostics.Stopwatch blockTransferTimer = new System.Diagnostics.Stopwatch();
        private long blockTransferBytes;

        public KernelReader(IKernelSession session, OSIDInfo pcmInfo, ILogger logger)
        {
            this.session = session;
            this.pcmInfo = pcmInfo;
            this.logger = logger;
        }

        /// <summary>
        /// Read the full contents of the PCM. Assumes the PCM is unlocked and we're ready to go.
        /// </summary>
        public async Task<Response<Stream>> ReadContents(CancellationToken cancellationToken, IProgress<ProgressUpdate>? progress = null)
        {
            try
            {
                if (!await this.session.Start(this.pcmInfo, KernelOperation.Read, kernelAlreadyRunning: false, cancellationToken))
                {
                    return Response.Create(
                        cancellationToken.IsCancellationRequested ? ResponseStatus.Cancelled : ResponseStatus.Error,
                        (Stream)null!);
                }

                int imageSize = EffectiveImageSize(this.pcmInfo, this.session.FlashChip, this.logger);
                this.logger.AddUserMessage("Reading " + (imageSize / 1024) + " KiB...");

                byte[] image = new byte[imageSize];
                Response<int> readResult = await this.ReadImage(image, imageSize, cancellationToken, progress);
                if (readResult.Status != ResponseStatus.Success)
                {
                    return Response.Create(readResult.Status, (Stream)null!);
                }

                this.logger.AddUserMessage("Read complete.");
                Utility.ReportRetryCount("Read", readResult.Value, imageSize, this.logger);

                ResponseStatus status = await this.session.Verify(image, imageSize, cancellationToken);
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
                // Always return the PCM to its stock OS, on every exit path, so the kernel is never
                // left running. Cleanup swallows its own errors, so this is safe while a cancellation
                // unwinds.
                await this.session.Commands.Cleanup(CancellationToken.None);
                this.logger.StatusUpdateReset();
            }
        }

        /// <summary>
        /// Fill the image a block at a time. Returns the total number of retries on success.
        /// </summary>
        private async Task<Response<int>> ReadImage(
            byte[] image, int imageSize, CancellationToken cancellationToken, IProgress<ProgressUpdate>? progress)
        {
            uint baseAddress = (uint)this.pcmInfo.ImageBaseAddress;
            int blockSize = this.session.MaxReadBlockSize;
            int retryCount = 0;

            // Anything below the read-start address is not readable (e.g. the E92 protected boot
            // block); leave it 0xFF. For most PCMs ReadStartAddress is 0, so this is a no-op.
            int readStart = this.pcmInfo.ReadStartAddress;
            for (int i = 0; i < readStart && i < imageSize; i++)
            {
                image[i] = 0xFF;
            }

            this.blockTransferTimer.Reset();
            this.blockTransferBytes = 0;

            for (int offset = readStart; offset < imageSize; offset += blockSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, retryCount);
                }

                await this.session.KeepAlive(cancellationToken);

                uint address = baseAddress + (uint)offset;
                int thisBlock = Math.Min(blockSize, imageSize - offset);

                Response<byte[]> block = await this.ReadBlock(address, thisBlock, cancellationToken);
                retryCount += block.RetryCount;

                if (block.Status != ResponseStatus.Success)
                {
                    this.logger.AddUserMessage(string.Format(
                        "Unable to read block at 0x{0:X6}: {1}", address, block.Status));
                    return Response.Create(block.Status, retryCount);
                }

                if (block.Value.Length != thisBlock)
                {
                    this.logger.AddUserMessage(string.Format(
                        "Expected {0} bytes, received {1} bytes.", thisBlock, block.Value.Length));
                    return Response.Create(ResponseStatus.Truncated, retryCount);
                }

                Buffer.BlockCopy(block.Value, 0, image, offset, thisBlock);
                this.blockTransferBytes += thisBlock;
                this.ReportProgress(progress, offset + thisBlock, imageSize, address);
                this.logger.StatusUpdateRetryCount(retryCount > 0
                    ? retryCount + (retryCount > 1 ? " Retries" : " Retry")
                    : string.Empty);
            }

            return Response.Create(ResponseStatus.Success, retryCount);
        }

        /// <summary>
        /// One block, retried. A dropped or garbled frame corrupts that block's reassembly; re-syncing
        /// and re-requesting is cheap, and the kernel re-reads idempotently.
        /// </summary>
        private async Task<Response<byte[]>> ReadBlock(uint address, int length, CancellationToken cancellationToken)
        {
            this.logger.AddDebugMessage(string.Format(
                "Reading from {0} / 0x{0:X}, length {1} / 0x{1:X}", address, length));

            int maxAttempts = this.session.MaxBlockAttempts;
            Response<byte[]> response = Response.Create(ResponseStatus.Error, Array.Empty<byte>());

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, Array.Empty<byte>(), attempt - 1);
                }

                // Time only the block round trip, not the re-sync delay between failed attempts.
                this.blockTransferTimer.Start();
                try
                {
                    response = await this.session.ReadMemoryBlock(address, length, cancellationToken);
                }
                catch (Exception exception)
                {
                    response = Response.Create(ResponseStatus.Error, Array.Empty<byte>());
                    this.logger.AddDebugMessage(string.Format(
                        "Block 0x{0:X6} attempt {1}/{2} threw: {3}", address, attempt, maxAttempts, exception.Message));
                }
                finally
                {
                    this.blockTransferTimer.Stop();
                }

                if (response.Status == ResponseStatus.Success)
                {
                    return Response.Create(response.Status, response.Value, attempt - 1);
                }

                if (attempt < maxAttempts)
                {
                    this.logger.AddUserMessage(string.Format(
                        "Block 0x{0:X6} did not transfer (attempt {1}/{2}, {3}); re-syncing and retrying.",
                        address, attempt, maxAttempts, response.Status));
                    try { this.session.ResetTransport(); } catch { /* best effort */ }
                    await Task.Delay(150, cancellationToken);
                }
            }

            return Response.Create(response.Status, Array.Empty<byte>(), maxAttempts);
        }

        /// <summary>
        /// How much to read. The profile size is the default, but the OSID database can be wrong or
        /// incomplete, so a chip the kernel actually identified wins: that way the whole chip is
        /// captured. The chip may be based above zero (the E92 reads its app region from 0x040000),
        /// so it is the readable span, not the raw chip size, that competes with the profile.
        /// </summary>
        /// <remarks>
        /// The P10 and P11 are the exception: they carry a 1MiB chip but only the lower 512KiB is
        /// wired up, so the profile size has to stand. This mirrors the same special case in
        /// KernelWriter, which deliberately writes a 512KiB image to a 1MiB chip.
        /// </remarks>
        public static int EffectiveImageSize(OSIDInfo pcmInfo, FlashChip? flashChip, ILogger logger)
        {
            int imageSize = pcmInfo.ImageSize;
            if (flashChip == null
                || pcmInfo.HardwareType == PcmType.P10
                || pcmInfo.HardwareType == PcmType.P11)
            {
                return imageSize;
            }

            int readable = (int)flashChip.Size - pcmInfo.ImageBaseAddress;
            if (readable <= 0 || readable == imageSize)
            {
                return imageSize;
            }

            logger.AddUserMessage(string.Format(
                "PCM Info image size is {0}KiB but the detected flash chip is {1}KiB. Reading {2}KiB.",
                imageSize / 1024, flashChip.Size / 1024, readable / 1024));
            return readable;
        }

        /// <remarks>
        /// Percentage is the 0..1 fraction the UI expects (ReadModel multiplies it by 100), not a
        /// 0..100 number.
        /// </remarks>
        private void ReportProgress(IProgress<ProgressUpdate>? progress, int bytesRead, int imageSize, uint address)
        {
            double fraction = imageSize > 0 ? (double)bytesRead / imageSize : 0;

            // Throughput is the block round-trip rate (see blockTransferTimer), not wall-clock time.
            double transferSeconds = this.blockTransferTimer.Elapsed.TotalSeconds;
            double rate = transferSeconds > 0 ? this.blockTransferBytes / transferSeconds : 0;

            string timeRemaining = string.Empty;
            if (rate > 0)
            {
                double secondsRemaining = (imageSize - bytesRead) / rate;
                timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString("mm\\:ss");
            }

            this.logger.StatusUpdateActivity($"Reading 0x{address:X6}");
            this.logger.StatusUpdatePercentDone($"{fraction * 100.0:0}%");
            this.logger.StatusUpdateProgressBar(fraction, true);
            this.logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
            this.logger.StatusUpdateKbps(rate > 0 ? $"{rate * 8.0 / 1000.0:0.00} Kbps" : string.Empty);

            progress?.Report(new ProgressUpdate
            {
                PayloadLength = bytesRead,
                TotalLength = imageSize,
                Address = $"0x{address:X6}",
                Percentage = fraction,
                Rate = rate,
                TimeRemaining = timeRemaining,
            });
        }
    }
}

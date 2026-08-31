// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The CAN side of <see cref="IKernelSession"/>: upload the kernel through GMLAN and read blocks
    /// through it. There is no speed switch and no loader, so starting is the upload alone.
    /// </summary>
    public class CanKernelSession : IKernelSession
    {
        // The kernel needs a moment after the start ack before it answers queries or reads.
        private const int KernelSettleMs = 300;

        private readonly Vehicle vehicle;
        private readonly CanCommands commands;
        private readonly ILogger logger;

        private OSIDInfo pcmInfo = null!;

        // Reported write throughput is the block round-trip rate: bytes moved / time in block
        // transfers only, so erase, verify, and idle time don't distort the figure.
        private readonly System.Diagnostics.Stopwatch blockTransferTimer = new System.Diagnostics.Stopwatch();
        private long blockTransferBytes;

        public CanKernelSession(Vehicle vehicle, CanCommands commands, ILogger logger)
        {
            this.vehicle = vehicle;
            this.commands = commands;
            this.logger = logger;
        }

        public IPcmCommands Commands => this.commands;

        public FlashChip? FlashChip { get; private set; }

        /// <remarks>
        /// A dropped or garbled frame corrupts one block's reassembly; re-syncing and re-requesting is
        /// cheap, and the kernel re-reads idempotently.
        /// </remarks>
        public int MaxBlockAttempts => 4;

        /// <summary>Fixed by each read kernel; see PcmInfo.KernelReadBlockSize.</summary>
        public int MaxReadBlockSize => this.pcmInfo.KernelReadBlockSize;

        // CAN always uploads: it has no "kernel already running" fast path (kernelAlreadyRunning is a
        // VPW recovery notion), so the flag is accepted for the interface and ignored.
        public async Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, bool kernelAlreadyRunning, CancellationToken cancellationToken)
        {
            this.pcmInfo = pcmInfo;
            this.vehicle.ClearDeviceMessageQueue();

            string kernelFile = pcmInfo.GetKernelFileName(operation);
            Response<byte[]> kernel = await this.vehicle.LoadKernelFromFile(kernelFile);
            if (kernel.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Failed to load CAN kernel: " + kernelFile);
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            this.logger.StatusUpdateActivity("Uploading kernel to PCM...");
            if (!await this.commands.UploadKernel(pcmInfo, kernel.Value, cancellationToken))
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    this.logger.AddUserMessage("Failed to upload kernel to PCM.");
                }

                return false;
            }

            this.logger.AddUserMessage("Kernel uploaded to PCM successfully.");
            await Task.Delay(KernelSettleMs, cancellationToken);

            Response<ulong> version = await this.commands.GetKernelVersion(cancellationToken);
            this.logger.AddUserMessage(version.Status == ResponseStatus.Success
                ? "Kernel version: " + Vehicle.FormatKernelVersion(version.Value)
                : "Kernel did not report a version (" + version.Status + "); continuing.");

            await this.IdentifyFlashChip(cancellationToken);
            return true;
        }

        /// <summary>
        /// An unidentified chip is not fatal here: the read still works, it just leaves the PCM
        /// profile's image size in place, and nothing on this bus needs the chip's memory ranges.
        /// </summary>
        private async Task IdentifyFlashChip(CancellationToken cancellationToken)
        {
            Response<uint> chipId = await this.commands.GetFlashId(cancellationToken);
            if (chipId.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Flash chip ID query failed (" + chipId.Status + "); continuing.");
                return;
            }

            try
            {
                this.FlashChip = FlashChip.Create(chipId.Value, this.logger);
                this.logger.AddUserMessage("Flash chip: " + this.FlashChip.ToString());
            }
            catch (Exception)
            {
                this.logger.AddUserMessage(string.Format("Flash chip ID {0:X8} is not in the known-chip table.", chipId.Value));
            }
        }

        public Task<Response<byte[]>> ReadMemoryBlock(uint address, int length, CancellationToken cancellationToken)
            => this.commands.ReadMemoryBlock(address, length, cancellationToken);

        /// <summary>The CAN kernel needs no traffic between blocks.</summary>
        public Task KeepAlive(CancellationToken cancellationToken) => Task.CompletedTask;

        public void ResetTransport() => this.vehicle.ClearDeviceMessageQueue();

        /// <summary>
        /// Validate the read image using the checks appropriate to its PCM type (E38 -> 2MB Sum/CVN
        /// tables; P05c -> 1MB parameter-block checksums; etc.). IdentifyAndValidate identifies the
        /// type and dispatches, so this is not hard-coded to E38/2MB.
        /// </summary>
        public Task<ResponseStatus> Verify(byte[] image, int imageSize, CancellationToken cancellationToken)
        {
            FileValidator validator = new FileValidator(image, this.logger);
            if (!validator.IdentifyAndValidate())
            {
                // A failed validation makes this a bad read (the caller saves it as *_badread).
                return Task.FromResult(ResponseStatus.Unverified);
            }

            this.logger.AddUserMessage("File operating system ID: " + validator.GetOsidFromImage());
            return Task.FromResult(ResponseStatus.Success);
        }

        // ---- Write operations (the former CAN writer's behavior, preserved) ----------------------

        public int MaxWriteBlockSize => this.commands.MaxFlashWriteBlockSize;

        public void BeginWritePass()
        {
            this.blockTransferTimer.Reset();
            this.blockTransferBytes = 0;
        }

        /// <summary>
        /// Compare each in-scope range's on-device CRC32 against the image's, recording both on the
        /// range. The desired CRC is computed locally the same way the kernel computes the actual one.
        /// Also prints the comparison table.
        /// </summary>
        public async Task<CrcVerificationResult> CompareRanges(
            byte[] image, BlockType relevantBlocks, uint effectiveImageSize, uint baseAddress, CancellationToken cancellationToken)
        {
            bool allMatch = true;

            const string formatString = "{0:X6}-{1:X6}  {2,-10:X8}  {3,-10:X8}  {4,-9}  {5}";
            this.logger.AddUserMessage("Calculating CRCs from file.");
            this.logger.AddUserMessage("Requesting CRCs from PCM.");
            this.logger.AddUserMessage(string.Format("{0,-13}  {1,-10}  {2,-10}  {3,-9}  {4}", "Range", "File CRC", "PCM CRC", "Verdict", "Purpose"));

            foreach (MemoryRange range in this.FlashChip!.MemoryRanges)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return CrcVerificationResult.Cancelled;
                }

                string rangeType = range.Type.ToString();

                if (((range.Type & relevantBlocks) == 0)
                    || (range.Address >= effectiveImageSize)
                    || (range.Address + range.Size <= (uint)this.pcmInfo.ReadStartAddress))
                {
                    this.logger.AddUserMessage(string.Format(
                        formatString, range.Address, range.Address + (range.Size - 1), "not needed", "not needed", "n/a", rangeType));
                    continue;
                }

                range.DesiredCrc = Gmlan.ComputeCrc32(image, (int)range.Address, (int)range.Size);

                Response<uint> actual = await this.commands.GetRangeCrc(baseAddress + range.Address, range.Size, cancellationToken);
                if (actual.Status == ResponseStatus.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    return CrcVerificationResult.Cancelled;
                }
                if (actual.Status != ResponseStatus.Success)
                {
                    return CrcVerificationResult.Timeout;
                }
                range.ActualCrc = actual.Value;

                bool match = range.DesiredCrc == range.ActualCrc;
                this.logger.AddUserMessage(string.Format(
                    formatString, range.Address, range.Address + (range.Size - 1),
                    range.DesiredCrc, range.ActualCrc, match ? "Same" : "Different", rangeType));

                if (!match)
                {
                    allMatch = false;
                }
            }

            return allMatch ? CrcVerificationResult.Verified : CrcVerificationResult.Mismatch;
        }

        public async Task<bool> EraseRange(MemoryRange range, uint baseAddress, CancellationToken cancellationToken)
        {
            this.logger.AddUserMessage("Erasing.");

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            Response<byte> eraseResponse = await this.commands.EraseFlashSector(baseAddress + range.Address, cancellationToken);
            if (eraseResponse.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage(string.Format("Unable to erase flash sector at 0x{0:X6}: {1}", range.Address, eraseResponse.Status));
                return false;
            }

            if (eraseResponse.Value != 0x00)
            {
                this.logger.AddUserMessage(string.Format("Unable to erase flash sector at 0x{0:X6}. Code: {1:X2}", range.Address, eraseResponse.Value));
                return false;
            }

            return true;
        }

        public async Task<Response<bool>> WriteRange(
            MemoryRange range, byte[] image, uint baseAddress, bool justTestWrite,
            DateTime startTime, uint totalSize, uint bytesRemaining, CancellationToken cancellationToken)
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

                this.logger.AddDebugMessage(string.Format(
                    "Sending payload with offset 0x{0:X6}, length 0x{1:X4}.", imageOffset, thisPayloadSize));

                TimeSpan elapsed = DateTime.Now - startTime;
                uint totalWritten = totalSize - bytesRemaining;
                uint bytesPerSecond = elapsed.TotalSeconds > 0 ? (uint)(totalWritten / elapsed.TotalSeconds) : 0;
                string timeRemaining = string.Empty;
                if (bytesPerSecond > 0)
                {
                    uint secondsRemaining = bytesRemaining / bytesPerSecond;
                    timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString("mm\\:ss");
                }

                // Throughput is the block round-trip rate (see blockTransferTimer), not wall-clock time.
                double transferSeconds = this.blockTransferTimer.Elapsed.TotalSeconds;
                double blockBytesPerSecond = transferSeconds > 0 ? this.blockTransferBytes / transferSeconds : 0;

                this.logger.StatusUpdateActivity($"Writing {thisPayloadSize} bytes to 0x{flashAddress:X6}");
                this.logger.StatusUpdatePercentDone((totalSize > 0 && totalWritten * 100 / totalSize > 0) ? $"{totalWritten * 100 / totalSize}%" : string.Empty);
                this.logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
                this.logger.StatusUpdateKbps((blockBytesPerSecond > 0) ? $"{blockBytesPerSecond * 8.00 / 1000.00:0.00} Kbps" : string.Empty);
                if (totalSize > 0)
                {
                    this.logger.StatusUpdateProgressBar((double)(totalWritten + (uint)thisPayloadSize) / totalSize, true);
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

        /// <summary>CAN has no post-failure recovery step.</summary>
        public Task AfterFailedWrite(BlockType relevantBlocks, WriteType writeType, CancellationToken cancellationToken)
            => Task.CompletedTask;

        /// <summary>CAN does not prompt for diagnostic logs.</summary>
        public void RequestDiagnostics(CancellationToken cancellationToken)
        {
        }

        /// <summary>CAN does not run a kernel-side OS check before writing.</summary>
        public Task<bool> VerifyOperatingSystem(
            FileValidator validator, bool needToCheckOperatingSystem, WriteType writeType, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}

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

        public async Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, CancellationToken cancellationToken)
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
    }
}

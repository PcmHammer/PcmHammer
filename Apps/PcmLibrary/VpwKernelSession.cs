// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The VPW side of <see cref="IKernelSession"/>: switch to 4X, upload the loader where the PCM
    /// needs one, run the kernel, and read blocks through it.
    /// </summary>
    public class VpwKernelSession : IKernelSession
    {
        private readonly Vehicle vehicle;
        private readonly Protocol protocol = new Protocol();
        private readonly ILogger logger;

        private OSIDInfo pcmInfo = null!;

        // The read timeout is set once, before the first block, as it was when this lived in the
        // reader. Re-sending it per block would put a device round trip in the hot loop.
        private bool blockTimeoutSet;

        /// <summary>Delay between CRC polls during verification.</summary>
        public int CrcPollingDelayMs { get; set; } = 50;

        /// <summary>
        /// Set for a recovery read. The PCM is in its boot loader with no operating system, so the 4X
        /// switch may go unanswered; that is expected and must not stop the read.
        /// </summary>
        public bool IsRecovery { get; set; }

        public VpwKernelSession(Vehicle vehicle, ILogger logger)
        {
            this.vehicle = vehicle;
            this.logger = logger;
        }

        public IPcmCommands Commands => this.vehicle;

        public FlashChip? FlashChip { get; private set; }

        public int MaxBlockAttempts => Vehicle.MaxSendAttempts;

        public int MaxReadBlockSize
        {
            get
            {
                // Leave room for the header and the block checksum.
                int blockSize = this.vehicle.DeviceMaxReceiveSize - 10 - 2;
                return Math.Min(blockSize, this.pcmInfo.KernelMaxBlockSize);
            }
        }

        public async Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, CancellationToken cancellationToken)
        {
            this.pcmInfo = pcmInfo;
            this.blockTimeoutSet = false;

            // Start with known state.
            await this.vehicle.ForceSendToolPresentNotification();
            this.vehicle.ClearDeviceMessageQueue();

            if (this.vehicle.Enable4xReadWrite)
            {
                // If the vehicle bus switches but the device does not, the bus has to time out back to
                // 1X and everything after this fails.
                if (!await this.vehicle.VehicleSetVPW4x(pcmInfo, VpwSpeed.FourX))
                {
                    if (!this.IsRecovery)
                    {
                        this.logger.AddUserMessage("Stopping here because we were unable to switch to 4X.");
                        return false;
                    }

                    this.logger.AddUserMessage("Recovery: 4X was refused, continuing at 1X. This will be slow.");
                }
            }
            else
            {
                this.logger.AddUserMessage("4X communications disabled by configuration.");
            }

            await this.vehicle.SendToolPresentNotification();

            if (pcmInfo.LoaderRequired
                && !await this.Upload(pcmInfo.LoaderFileName, "loader", cancellationToken))
            {
                return false;
            }

            if (!await this.Upload(pcmInfo.GetKernelFileName(operation), "kernel", cancellationToken))
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await this.ReportIacDriver(pcmInfo, cancellationToken);

            await this.vehicle.SendToolPresentNotification();
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // The verifier works from the chip's memory ranges, so on this bus a chip we cannot
            // identify is fatal rather than something to continue past.
            if (pcmInfo.FlashIDSupport)
            {
                Response<UInt32> chipId = await this.vehicle.QueryFlashChipId(cancellationToken);
                if (chipId.Status != ResponseStatus.Success)
                {
                    return false;
                }

                this.FlashChip = FlashChip.Create(chipId.Value, this.logger);
                this.logger.AddUserMessage("Flash chip: " + this.FlashChip.ToString());
            }

            return true;
        }

        public async Task<Response<byte[]>> ReadMemoryBlock(uint address, int length, CancellationToken cancellationToken)
        {
            if (!this.blockTimeoutSet)
            {
                await this.vehicle.SetDeviceTimeout(TimeoutScenario.ReadMemoryBlock);
                this.blockTimeoutSet = true;
            }

            return await this.vehicle.ReadMemory(
                () => this.protocol.CreateReadRequest((int)address, length),
                message => this.protocol.ParsePayload(message, length, (int)address),
                cancellationToken);
        }

        /// <remarks>
        /// The read kernel needs a short message between blocks for reasons unknown. Without it, it
        /// will RX 2 messages then drop one.
        /// </remarks>
        public Task KeepAlive(CancellationToken cancellationToken)
            => this.vehicle.ForceSendToolPresentNotification();

        public void ResetTransport() => this.vehicle.ClearDeviceMessageQueue();

        public async Task<ResponseStatus> Verify(byte[] image, int imageSize, CancellationToken cancellationToken)
        {
            if (!this.pcmInfo.FlashCRCSupport || this.FlashChip == null)
            {
                return ResponseStatus.Success;
            }

            this.logger.AddUserMessage("Starting verification...");

            CKernelVerifier verifier = new CKernelVerifier(
                image,
                this.FlashChip.MemoryRanges,
                this.vehicle,
                this.protocol,
                this.pcmInfo,
                (UInt32)imageSize,
                this.logger)
            {
                PollingDelayMs = this.CrcPollingDelayMs,
            };

            this.logger.StatusUpdateReset();

            CrcVerificationResult result = await verifier.CompareRanges(image, BlockType.All, cancellationToken);
            switch (result)
            {
                case CrcVerificationResult.Verified:
                    this.logger.AddUserMessage("The contents of the file match the contents of the PCM.");
                    return ResponseStatus.Success;

                case CrcVerificationResult.Timeout:
                    return ResponseStatus.Unverified;

                default:
                    this.logger.AddUserMessage("##############################################################################");
                    this.logger.AddUserMessage("There are errors in the data that was read from the PCM. Do not use this file.");
                    this.logger.AddUserMessage("##############################################################################");
                    return ResponseStatus.Success;
            }
        }

        private async Task<bool> Upload(string fileName, string description, CancellationToken cancellationToken)
        {
            Response<byte[]> file = await this.vehicle.LoadKernelFromFile(fileName);
            if (file.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Failed to load " + description + " from file.");
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await this.vehicle.SendToolPresentNotification();

            if (!await this.vehicle.PCMExecute(this.pcmInfo, file.Value, cancellationToken))
            {
                this.logger.AddUserMessage("Failed to upload " + description + " to PCM");
                return false;
            }

            this.logger.AddUserMessage(char.ToUpper(description[0]) + description.Substring(1) + " uploaded to PCM successfully.");
            return true;
        }

        /// <summary>
        /// Does this PCM carry the IAC (idle air control) driver chip? Ask the kernel, which probes it
        /// live over the QSPI bus (P01/P59).
        /// </summary>
        private async Task ReportIacDriver(OSIDInfo pcmInfo, CancellationToken cancellationToken)
        {
            if (!pcmInfo.DetectIAC)
            {
                return;
            }

            Response<ushort> response = await this.vehicle.QueryIACDriver(cancellationToken);
            if (response.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Unable to determine whether the IAC driver chip is present.");
                return;
            }

            this.logger.AddDebugMessage(string.Format("IAC probe raw QSPI response: 0x{0:X4}", response.Value));
            this.logger.AddUserMessage(IacDriverPresent(response.Value)
                ? "This PCM contains the IAC driver chip (DBW+DBC support)."
                : "This PCM does not contain the IAC driver chip (DBW Only, no DBC).");
        }

        /// <summary>
        /// Decide whether the IAC driver chip is present from the QSPI probe response.
        /// </summary>
        /// <remarks>
        /// The low byte is the 8-bit value the drive sends back over the QSPI.
        /// An empty footprint reads back the high nibble all set, 0xF0, because nothing
        /// drives the bus; a populated chip drives real status (high nibble never all-set, e.g. 0xC7
        /// or 0xDF). A floating-low or no-response read (0x00) is also treated as not present.
        /// Samples: empty board = 0xF0 x12; populated board = 0xDF then 0xC7 x11.
        /// </remarks>
        private static bool IacDriverPresent(ushort raw)
        {
            byte response = (byte)(raw & 0x00FF);
            if (response == 0x00)
            {
                return false;
            }

            return (response & 0xF0) != 0xF0;
        }
    }
}

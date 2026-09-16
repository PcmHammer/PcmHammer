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

        public async Task<bool> Start(OSIDInfo pcmInfo, KernelOperation operation, bool kernelAlreadyRunning, CancellationToken cancellationToken)
        {
            this.pcmInfo = pcmInfo;
            this.blockTimeoutSet = false;

            // Start with known state.
            await this.vehicle.ForceSendToolPresentNotification();
            this.vehicle.ClearDeviceMessageQueue();

            // A kernel already running (a write started after a live kernel was found) needs no
            // switch or upload; only the chip is (re)identified below.
            if (!kernelAlreadyRunning)
            {
                if (this.vehicle.Enable4xReadWrite)
                {
                    // If the vehicle bus switches but the device does not, the bus has to time out back
                    // to 1X and everything after this fails.
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
            }

            // The IAC probe is a read-time report; a write never needs it, and on a P01/P59 the probe
            // crashes the kernel, so it must not run on the write path.
            if (operation == KernelOperation.Read)
            {
                await this.ReportIacDriver(pcmInfo, cancellationToken);
            }

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

            KernelVerifier verifier = new KernelVerifier(
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

        // ---- Write operations (the former VPW writer's behavior, preserved) ----------------------

        /// <summary>Ask the running kernel for the OSID and halt on a mismatch when required.</summary>
        public async Task<bool> VerifyOperatingSystem(
            FileValidator validator, bool needToCheckOperatingSystem, WriteType writeType, CancellationToken cancellationToken)
        {
            await this.vehicle.SendToolPresentNotification();
            await this.vehicle.SetDeviceTimeout(TimeoutScenario.ReadProperty);
            Response<UInt32> osidResponse = await this.vehicle.QueryOperatingSystemIdFromKernel(cancellationToken);
            if (needToCheckOperatingSystem && (osidResponse.Status != ResponseStatus.Success))
            {
                // The kernel seems broken. This shouldn't happen, but if it does, halt.
                this.logger.AddUserMessage("The kernel did not respond to operating system ID query.");
                return false;
            }

            Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, writeType, this.logger, out bool shouldHalt);
            return !(needToCheckOperatingSystem && shouldHalt);
        }

        public int MaxWriteBlockSize => this.vehicle.DeviceMaxFlashWriteSendSize - 12;

        /// <summary>VPW reports throughput from wall-clock time, so there is no per-pass timer to reset.</summary>
        public void BeginWritePass()
        {
        }

        public async Task<CrcVerificationResult> CompareRanges(
            byte[] image, BlockType relevantBlocks, uint effectiveImageSize, uint baseAddress, CancellationToken cancellationToken)
        {
            // baseAddress is unused on VPW (ImageBaseAddress is 0); the verifier queries range.Address.
            KernelVerifier verifier = new KernelVerifier(
                image,
                this.FlashChip!.MemoryRanges,
                this.vehicle,
                this.protocol,
                this.pcmInfo,
                effectiveImageSize,
                this.logger)
            {
                PollingDelayMs = this.CrcPollingDelayMs,
            };

            return await verifier.CompareRanges(image, relevantBlocks, cancellationToken);
        }

        /// <summary>
        /// Erase a block in flash memory. Each FlashChip MemoryRange is one hardware erase sector.
        /// baseAddress is unused on VPW.
        /// </summary>
        public async Task<bool> EraseRange(MemoryRange range, uint baseAddress, CancellationToken cancellationToken)
        {
            this.logger.AddUserMessage("Erasing.");

            await this.vehicle.SetDeviceTimeout(TimeoutScenario.EraseMemoryBlock);
            Query<byte> eraseRequest = this.vehicle.CreateQuery<byte>(
                () => this.protocol.CreateFlashEraseBlockRequest(range.Address),
                this.protocol.ParseFlashEraseBlock,
                cancellationToken);

            eraseRequest.MaxTimeouts = 3;
            Response<byte> eraseResponse = await eraseRequest.Execute();

            if (eraseResponse.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Unable to erase flash memory: " + eraseResponse.Status.ToString());
                this.RequestDiagnostics(cancellationToken);
                return false;
            }

            if (eraseResponse.Value != 0x00)
            {
                this.logger.AddUserMessage("Unable to erase flash memory. Code: " + eraseResponse.Value.ToString("X2"));
                this.RequestDiagnostics(cancellationToken);
                return false;
            }

            return true;
        }

        /// <summary>Copy a single memory range to the PCM. baseAddress is unused on VPW.</summary>
        public async Task<Response<bool>> WriteRange(
            MemoryRange range, byte[] image, uint baseAddress, bool justTestWrite,
            DateTime startTime, uint totalSize, uint bytesRemaining, CancellationToken cancellationToken)
        {
            int retryCount = 0;
            int devicePayloadSize = this.MaxWriteBlockSize; // headers use 10 bytes, sum uses 2 bytes.
            for (int index = 0; index < range.Size; index += devicePayloadSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, false, retryCount);
                }

                await this.vehicle.SendToolPresentNotification();

                int startAddress = (int)(range.Address + index);
                UInt32 thisPayloadSize = (UInt32)Math.Min(devicePayloadSize, (int)range.Size - index);

                this.logger.AddDebugMessage(string.Format(
                    "Sending payload with offset 0x{0:X4}, start address 0x{1:X6}, length 0x{2:X4}.",
                    index, startAddress, thisPayloadSize));

                Message payloadMessage = this.protocol.CreateBlockMessage(
                    image, startAddress, (int)thisPayloadSize, startAddress,
                    justTestWrite ? BlockCopyType.TestWrite : BlockCopyType.Copy);

                string timeRemaining = string.Empty;
                TimeSpan elapsed = DateTime.Now - startTime;
                UInt32 totalWritten = totalSize - bytesRemaining;
                UInt32 bytesPerSecond = (UInt32)(totalWritten / elapsed.TotalSeconds);
                if (bytesPerSecond > 0)
                {
                    UInt32 secondsRemaining = (UInt32)(bytesRemaining / bytesPerSecond);
                    timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString("mm\\:ss");
                }

                this.logger.StatusUpdateActivity($"Writing {thisPayloadSize} bytes to 0x{startAddress:X6}");
                this.logger.StatusUpdatePercentDone((totalWritten * 100 / totalSize > 0) ? $"{totalWritten * 100 / totalSize}%" : string.Empty);
                this.logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
                this.logger.StatusUpdateKbps((bytesPerSecond > 0) ? $"{(double)bytesPerSecond * 8.00 / 1000.00:0.00} Kbps" : string.Empty);
                this.logger.StatusUpdateProgressBar((double)(totalWritten + thisPayloadSize) / totalSize, true);

                await this.vehicle.SetDeviceTimeout(TimeoutScenario.WriteMemoryBlock);

                // WritePayload contains a retry loop, so a failure here does not need retrying.
                Response<bool> response = await this.vehicle.WritePayload(payloadMessage, cancellationToken);
                if (response.Status != ResponseStatus.Success)
                {
                    return Response.Create(ResponseStatus.Error, false, response.RetryCount);
                }

                bytesRemaining -= thisPayloadSize;
                retryCount += response.RetryCount;
            }

            return Response.Create(ResponseStatus.Success, true, retryCount);
        }

        /// <summary>
        /// On a failed calibration write, erase calibration so the PCM drops into recovery mode and
        /// can be re-flashed. VPW-specific; the CAN session has no equivalent.
        /// </summary>
        public async Task AfterFailedWrite(BlockType relevantBlocks, WriteType writeType, CancellationToken cancellationToken)
        {
            if (writeType != WriteType.Calibration || this.FlashChip == null)
            {
                return;
            }

            this.logger.AddUserMessage("Erasing Calibration to force recovery mode.");
            this.logger.AddUserMessage("");

            foreach (MemoryRange range in this.FlashChip.MemoryRanges)
            {
                if (range.Type == BlockType.Calibration)
                {
                    await this.EraseRange(range, 0, cancellationToken);
                }
            }
        }

        /// <summary>Ask the user for diagnostic logs, unless they cancelled.</summary>
        public void RequestDiagnostics(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this.logger.AddUserMessage("Select the debug tab, click anywhere in the text,");
                this.logger.AddUserMessage("press Ctrl+A to select the text, and Ctrl+C to");
                this.logger.AddUserMessage("copy the text. Press Ctrl+V to paste that content");
                this.logger.AddUserMessage("content into your forum post.");
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

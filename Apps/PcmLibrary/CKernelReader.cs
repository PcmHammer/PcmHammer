// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Reader classes use a kernel to read the entire flash memory.
    /// </summary>
    public class CKernelReader
    {
        private readonly Vehicle vehicle;
        private readonly OSIDInfo pcmInfo;
        private readonly Protocol protocol;
        private readonly ILogger logger;

        public int CrcPollingDelayMs { get; set; } = 50;

        public CKernelReader(Vehicle vehicle, OSIDInfo pcmInfo, ILogger logger)
        {
            this.vehicle = vehicle;
            this.pcmInfo = pcmInfo;

            // This seems wrong... Some alternatives:
            // a) Have the caller pass in the message factory and message-parser methods
            // b) Have the caller pass in a smaller KernelProtocol class - with subclasses for each kernel - 
            //    This would only make sense if it turns out that this one reader class can handle multiple kernels.
            // c) Just create a smaller KernelProtocol class here, for the kernel that this class is intended for.
            this.protocol = new Protocol();

            this.logger = logger;
        }

        /// <summary>
        /// Read the full contents of the PCM.
        /// Assumes the PCM is unlocked and we're ready to go.
        /// </summary>
        public async Task<Response<Stream>> ReadContents(CancellationToken cancellationToken, IProgress<ProgressUpdate>? progress = null)
        {
            try
            {
                // Start with known state.
                await this.vehicle.ForceSendToolPresentNotification();
                this.vehicle.ClearDeviceMessageQueue();

                // Switch to 4x, if possible. But continue either way.
                if (this.vehicle.Enable4xReadWrite)
                {
                    // if the vehicle bus switches but the device does not, the bus will need to time out to revert back to 1x, and the next steps will fail.
                    if (!await this.vehicle.VehicleSetVPW4x(this.pcmInfo, VpwSpeed.FourX))
                    {
                        logger.AddUserMessage("Stopping here because we were unable to switch to 4X.");
                        return Response.Create(ResponseStatus.Error, (Stream)null!);
                    }
                }
                else
                {
                    logger.AddUserMessage("4X communications disabled by configuration.");
                }

                await this.vehicle.SendToolPresentNotification();

                Response<byte[]> response;

                // Execute kernel loader, if required
                if (this.pcmInfo.LoaderRequired)
                {
                    response = await vehicle.LoadKernelFromFile(this.pcmInfo.LoaderFileName);
                    if (response.Status != ResponseStatus.Success)
                    {
                        logger.AddUserMessage("Failed to load loader from file.");
                        return new Response<Stream>(response.Status, null!);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }

                    await this.vehicle.SendToolPresentNotification();

                    if (!await this.vehicle.PCMExecute(this.pcmInfo, response.Value, cancellationToken))
                    {
                        logger.AddUserMessage("Failed to upload loader to PCM");

                        return new Response<Stream>(
                            cancellationToken.IsCancellationRequested ? ResponseStatus.Cancelled : ResponseStatus.Error,
                            null!);
                    }

                    logger.AddUserMessage("Loader uploaded to PCM successfully.");
                }

                // execute read kernel
                response = await vehicle.LoadKernelFromFile(this.pcmInfo.KernelFileName);
                if (response.Status != ResponseStatus.Success)
                {
                    logger.AddUserMessage("Failed to load kernel from file.");
                    return new Response<Stream>(response.Status, null!);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                }

                await this.vehicle.SendToolPresentNotification();

                if (!await this.vehicle.PCMExecute(this.pcmInfo, response.Value, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }

                    logger.AddUserMessage("Failed to upload kernel to PCM");

                    return Response.Create(ResponseStatus.Error, (Stream)null!);
                }

                logger.AddUserMessage("Kernel uploaded to PCM successfully. Requesting data...");

                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                }

                // Which flash chip?
                await this.vehicle.SendToolPresentNotification();

                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                }

                FlashChip flashChip = FlashChip.Create(0x12345678, this.logger);
                if (this.pcmInfo.FlashIDSupport)
                {
                    Response<UInt32> chipIdResponse = await this.vehicle.QueryFlashChipId(cancellationToken);
                    if (chipIdResponse.Status == ResponseStatus.Cancelled)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }

                    if (chipIdResponse.Status != ResponseStatus.Success)
                    {
                        return Response.Create(ResponseStatus.Error, (Stream)null!);
                    }

                    flashChip = FlashChip.Create(chipIdResponse.Value, this.logger);
                    logger.AddUserMessage("Flash chip: " + flashChip.ToString());
                }

                await this.vehicle.SetDeviceTimeout(TimeoutScenario.ReadMemoryBlock);

                
                // The read length normally comes from PCM Info (which is keyed off the OSID lookup).
                // But the OSID database can be wrong or incomplete. When the kernel actually reported
                // a flash chip ID we trust the detected chip's size over PCM Info, so we always capture
                // the whole chip. PCM Info stays the source for the security algorithm and as the fallback
                // when there's no chip ID.
                //
                // EXCEPTION: P10/P11 carry a 1MiB chip but only the lower 512KiB is used for
                // those we MUST keep the PCM Info size. (Mirrors the P10/P11 special-case in
                // CKernelWriter, which deliberately writes a 512KiB image to a 1MiB chip.)
                int imageSize = pcmInfo.ImageSize;
                bool chipLargerThanUsableImage =
                    pcmInfo.HardwareType == PcmType.P10 || pcmInfo.HardwareType == PcmType.P11;
                if (this.pcmInfo.FlashIDSupport && flashChip.Size > 0 &&
                    (int)flashChip.Size != imageSize && !chipLargerThanUsableImage)
                {
                    logger.AddUserMessage(
                        string.Format(
                            "PCM Info image size is {0}KiB but the detected flash chip is {1}KiB. Reading the full chip.",
                            imageSize / 1024,
                            flashChip.Size / 1024));
                    imageSize = (int)flashChip.Size;
                }

                int retryCount = 0;
                int startAddress = 0;

                //startAddress = 0x0000; //uncomment to read a portion only when testing
                //imageSize = 0xFFFF;

                byte[] image = new byte[imageSize];
                int bytesRemaining = imageSize;
                int blockSize = this.vehicle.DeviceMaxReceiveSize - 10 - 2; // allow space for the header and block checksum
                if (blockSize > this.pcmInfo.KernelMaxBlockSize)
                {
                    blockSize = this.pcmInfo.KernelMaxBlockSize;
                }

                DateTime startTime = DateTime.MaxValue;
                while (startAddress < imageSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, (Stream)null!);
                    }

                    // The read kernel needs a short message here for reasons unknown. Without it, it will RX 2 messages then drop one.
                    await this.vehicle.ForceSendToolPresentNotification();

                    if (startAddress + blockSize > imageSize)
                    {
                        blockSize = imageSize - startAddress;
                    }

                    if (blockSize < 1)
                    {
                        logger.AddUserMessage("Image download complete");
                        break;
                    }

                    if (startTime == DateTime.MaxValue)
                    {
                        startTime = DateTime.Now;
                    }

                    Response<bool> readResponse = await TryReadBlock(
                        image, 
                        blockSize, 
                        startAddress,
                        startTime,
                        cancellationToken,
                        progress);
                    if (readResponse.Status != ResponseStatus.Success)
                    {
                        logger.AddUserMessage(
                            string.Format(
                                "Unable to read block from {0} to {1}",
                                startAddress,
                                (startAddress + blockSize) - 1));
                        return new Response<Stream>(ResponseStatus.Error, null!);
                    }

                    startAddress += blockSize;
                    retryCount += readResponse.RetryCount;

                    logger.StatusUpdateRetryCount((retryCount > 0) ? retryCount.ToString() + ((retryCount > 1) ? " Retries" : " Retry") : string.Empty);
                }

                logger.AddUserMessage("Read complete.");
                Utility.ReportRetryCount("Read", retryCount, imageSize, this.logger);

                if (this.pcmInfo.FlashCRCSupport && this.pcmInfo.FlashIDSupport)
                {
                    logger.AddUserMessage("Starting verification...");

                    CKernelVerifier verifier = new CKernelVerifier(
                        image,
                        flashChip.MemoryRanges,
                        this.vehicle,
                        this.protocol,
                        this.pcmInfo,
                        (UInt32)imageSize,
                        this.logger)
                    {
                        PollingDelayMs = this.CrcPollingDelayMs,
                    };

                    logger.StatusUpdateReset();

                    CrcVerificationResult verificationResult = await verifier.CompareRanges(
                        image,
                        BlockType.All,
                        cancellationToken);

                    if (verificationResult == CrcVerificationResult.Verified)
                    {
                        logger.AddUserMessage("The contents of the file match the contents of the PCM.");
                    }
                    else if (verificationResult == CrcVerificationResult.Timeout)
                    {
                        MemoryStream unverifiedStream = new MemoryStream(image);
                        return new Response<Stream>(ResponseStatus.Unverified, unverifiedStream);
                    }
                    else
                    {
                        logger.AddUserMessage("##############################################################################");
                        logger.AddUserMessage("There are errors in the data that was read from the PCM. Do not use this file.");
                        logger.AddUserMessage("##############################################################################");
                    }
                }

                MemoryStream stream = new MemoryStream(image);
                return new Response<Stream>(ResponseStatus.Success, stream);
            }
            catch(Exception exception)
            {
                logger.AddUserMessage("Something went wrong. " + exception.Message);
                logger.AddDebugMessage(exception.ToString());
                return new Response<Stream>(ResponseStatus.Error, null!);
            }
            finally
            {
                await this.vehicle.Cleanup();
                logger.StatusUpdateReset();
            }
        }

        /// <summary>
        /// Try to read a block of PCM memory.
        /// </summary>
        private async Task<Response<bool>> TryReadBlock(
            byte[] image, 
            int length, 
            int startAddress, 
            DateTime startTime,
            CancellationToken cancellationToken,
             IProgress<ProgressUpdate>? progress)
        {
            logger.AddDebugMessage(string.Format("Reading from {0} / 0x{0:X}, length {1} / 0x{1:X}", startAddress, length));

            int retryCount = 0;
            for (; retryCount < Vehicle.MaxSendAttempts; retryCount++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Response<byte[]> readResponse = await this.vehicle.ReadMemory(
                    () => this.protocol.CreateReadRequest(startAddress, length),
                    (payloadMessage) => this.protocol.ParsePayload(payloadMessage, length, startAddress),
                    cancellationToken);

                if(readResponse.Status != ResponseStatus.Success)
                {
                    logger.AddDebugMessage("Unable to read segment: " + readResponse.Status);
                    continue;
                }

                byte[] payload = readResponse.Value;

                if (payload.Length != length)
                {
                    logger.AddUserMessage(
                        string.Format(
                            "Expected {0} bytes, received {1} bytes.",
                            length,
                            payload.Length));
                    return Response.Create(ResponseStatus.Truncated, false);
                }

                Buffer.BlockCopy(payload, 0, image, startAddress, payload.Length);

                TimeSpan elapsed = DateTime.Now - startTime;
                string timeRemaining = string.Empty;

                UInt32 bytesPerSecond = 0;
                UInt32 bytesRemaining = 0;

                bytesPerSecond = (UInt32)(startAddress / elapsed.TotalSeconds);
                bytesRemaining = (UInt32)(image.Length - startAddress);

                // Don't divide by zero.
                if (bytesPerSecond > 0)
                {
                    UInt32 secondsRemaining = (UInt32)(bytesRemaining / bytesPerSecond);
                    timeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString("mm\\:ss");
                }
                if (progress != null)
                {
                    ProgressUpdate update = new ProgressUpdate
                    {
                        Address = startAddress.ToString("X6"),
                        PayloadLength = payload.Length,
                        TotalLength = image.Length,
                        Percentage = ((double)startAddress + (double)payload.Length) / (double)image.Length,
                        Rate = bytesPerSecond > 0 ? (double)bytesPerSecond * 8.00 / 1000.00 : 0.00,
                        TimeRemaining = timeRemaining
                    };
                    progress.Report(update);
                }
                else
                {
                    logger.StatusUpdateActivity($"Reading {payload.Length} bytes from 0x{startAddress:X6}");
                    logger.StatusUpdatePercentDone((startAddress * 100 / image.Length > 0) ? $"{startAddress * 100 / image.Length}%" : string.Empty);
                    logger.StatusUpdateTimeRemaining($"T-{timeRemaining}");
                    logger.StatusUpdateKbps((bytesPerSecond > 0) ? $"{(double)bytesPerSecond * 8.00 / 1000.00:0.00} Kbps" : string.Empty);
                    logger.StatusUpdateProgressBar((double)(startAddress + payload.Length) / image.Length, true);
                }

                return Response.Create(ResponseStatus.Success, true, retryCount);
            }

            return Response.Create(ResponseStatus.Error, false, retryCount);
        }
    }
}

using PcmHacking.ECU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public enum CrcVerificationResult
    {
        Verified,
        Mismatch,
        Timeout,
        Cancelled,
    }

    public class CKernelVerifier
    {
        private readonly byte[] image;
        private readonly IEnumerable<MemoryRange> ranges;
        private readonly Vehicle vehicle;
        private readonly Protocol protocol;
        private readonly ECUBase pcmInfo;
        private readonly ILogger logger;
        private readonly IProgress<ProgressUpdate> progress;

        public CKernelVerifier(
            byte[] image, 
            IEnumerable<MemoryRange> ranges, 
            Vehicle vehicle, 
            Protocol protocol, 
            ECUBase pcmInfo,
            ILogger logger,
            IProgress<ProgressUpdate> progress)
        {
            this.image = image;
            this.ranges = ranges;
            this.vehicle = vehicle;
            this.protocol = protocol;
            this.pcmInfo = pcmInfo;
            this.logger = logger;
            this.progress = progress;
        }

        /// <summary>
        /// Get the CRC for each address range in the file that the user wants to flash.
        /// </summary>
        private void GetCrcFromImage()
        {
            Crc crc = new Crc();
            foreach (MemoryRange range in this.ranges)
            {
                if (range.Address < pcmInfo.ImageSize) // P10 does not use the whole chip
                {
                    range.DesiredCrc = crc.GetCrc(this.image, range.Address, range.Size);
                }
            }
        }

        /// <summary>
        /// Compare CRCs from the file to CRCs from the PCM.
        /// </summary>
        public async Task<CrcVerificationResult> CompareRanges(byte[] image, BlockType blockTypes, CancellationToken cancellationToken)
        {
            // This only takes a fraction of a second.
            logger.AddUserMessage("Calculating CRCs from file.");
            this.GetCrcFromImage();

            bool anyTimeout = false;
            bool anyMismatch = false;

            await this.vehicle.SendToolPresentNotification();
            await this.vehicle.SetDeviceTimeout(TimeoutScenario.ReadCrc);

            logger.AddUserMessage("Requesting CRCs from PCM.");
            logger.AddUserMessage("\tRange\t\tFile CRC\t\tPCM CRC\tVerdict\tPurpose");

            foreach (MemoryRange range in this.ranges)
            {
                string formatString = "{0:X6}-{1:X6}\t{2:X8}\t{3:X8}\t{4}\t{5}";
                string range_type = pcmInfo.IsSupportedWriteBySegment ? range.Type.ToString() : "General";

                if (((range.Type & blockTypes) == 0) || (range.Address >= this.pcmInfo.ImageSize))
                {
                    this.logger.AddUserMessage(string.Format(formatString, range.Address, range.Address + (range.Size - 1), "not needed", "not needed", "n/a", range_type));
                    continue;
                }

                await this.vehicle.SendToolPresentNotification();
                this.vehicle.ClearDeviceMessageQueue();
                progress.Report(new ProgressUpdate { Activity = $"Processing CRC: range {range.Address:X6}-{range.Address + (range.Size - 1):X6}" });
                //logger.StatusUpdateActivity($"Processing CRC: range {range.Address:X6}-{range.Address + (range.Size - 1):X6}");

                // For C Kernels each poll of the PCM causes it to CRC 16kb of segment data.
                // When the segment sum is available it is returned. Logged highs of 38 polls on a 1m P12.
                int retryDelay = 50;
                bool success = false;
                int consecutiveTimeouts = 0;
                UInt32 crc = 0;

                Message query = this.protocol.CreateCrcQuery(range.Address, range.Size);
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return CrcVerificationResult.Cancelled;
                    }

                    await this.vehicle.SendToolPresentNotification();

                    if (!await this.vehicle.SendMessage(query))
                    {
                        this.logger.AddUserMessage($"CRC query failed reading range {range.Address.ToString("X8")} / {range.Size.ToString("X8")}");
                        continue;
                    }

                    Message? response = null;
                    while (true)
                    {
                        response = await this.vehicle.ReceiveMessage();
                        if (response == null)
                        {
                            consecutiveTimeouts++;
                            if (consecutiveTimeouts >= 6)
                            {
                                string detail = anyTimeout ? "" : " Kernel may have crashed.";
                                this.logger.AddUserMessage($"PCM stopped responding during CRC check at {range.Address:X8} / {range.Size:X8}.{detail}");
                                anyTimeout = true;
                                break;
                            }
                            this.logger.AddDebugMessage($"CRC no response, re-querying {range.Address.ToString("X8")} / {range.Size.ToString("X8")}");
                            await Task.Delay(retryDelay);
                            continue;
                        }
                        break;
                    }

                    consecutiveTimeouts = 0;

                    Response<UInt32> crcResponse = this.protocol.ParseCrc(response, range.Address, range.Size);
                    if (crcResponse.Status != ResponseStatus.Success)
                    {
                        await Task.Delay(retryDelay);
                        continue;
                    }
                    success = true;
                    crc = crcResponse.Value;
                    break;
                }
                progress.Report(new ProgressUpdate { Activity = $"Finished CRC calulations.", Percentage = 0, ProgressBarVisible = true });
                logger.StatusUpdateProgressBar(0, false);

                if (!success)
                {
                    if (!anyTimeout)
                    {
                        this.logger.AddUserMessage("Unable to get CRC for memory range " + range.Address.ToString("X8") + " / " + range.Size.ToString("X8"));
                    }
                    anyMismatch = true;
                    continue;
                }

                this.vehicle.ClearDeviceMessageQueue();

                range.ActualCrc = crc;

                bool match = range.DesiredCrc == range.ActualCrc;
                if (!match) anyMismatch = true;

                this.logger.AddUserMessage(string.Format(formatString, range.Address, range.Address + (range.Size - 1), range.DesiredCrc, range.ActualCrc, match ? "Same" : "Different", range_type));
            }

            await this.vehicle.SendToolPresentNotification();
            this.vehicle.ClearDeviceMessageQueue();

            if (anyTimeout) return CrcVerificationResult.Timeout;
            if (anyMismatch) return CrcVerificationResult.Mismatch;
            return CrcVerificationResult.Verified;
        }
    }
}

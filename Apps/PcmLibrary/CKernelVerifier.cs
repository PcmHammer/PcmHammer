using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class CKernelVerifier
    {
        private readonly byte[] image;
        private readonly IEnumerable<MemoryRange> ranges;
        private readonly Vehicle vehicle;
        private readonly Protocol protocol;
        private readonly OSIDInfo pcmInfo;
        private readonly ILogger logger;

        public CKernelVerifier(
            byte[] image, 
            IEnumerable<MemoryRange> ranges, 
            Vehicle vehicle, 
            Protocol protocol, 
            OSIDInfo pcmInfo,
            ILogger logger)
        {
            this.image = image;
            this.ranges = ranges;
            this.vehicle = vehicle;
            this.protocol = protocol;
            this.pcmInfo = pcmInfo;
            this.logger = logger;
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
        public async Task<bool> CompareRanges(byte[] image, BlockType blockTypes, CancellationToken cancellationToken)
        {
            // This only takes a fraction of a second.
            logger.AddUserMessage("Calculating CRCs from file.");
            this.GetCrcFromImage();

            bool successForAllRanges = true;

            await this.vehicle.SendToolPresentNotification();
            await this.vehicle.SetDeviceTimeout(TimeoutScenario.ReadCrc);

            logger.AddUserMessage("Requesting CRCs from PCM.");
            logger.AddUserMessage("\tRange\t\tFile CRC\t\tPCM CRC\tVerdict\tPurpose");

            foreach (MemoryRange range in this.ranges)
            {
                string formatString = "{0:X6}-{1:X6}\t{2:X8}\t{3:X8}\t{4}\t{5}";

                if (((range.Type & blockTypes) == 0) || (range.Address >= this.pcmInfo.ImageSize))
                {
                    this.logger.AddUserMessage(
                    string.Format(
                        formatString,
                        range.Address,
                        range.Address + (range.Size - 1),
                        "not needed",
                        "not needed",
                        "n/a",
                        range.Type));
                    continue;
                }

                await this.vehicle.SendToolPresentNotification();
                this.vehicle.ClearDeviceMessageQueue();

                Message query = this.protocol.CreateCrcQuery(range.Address, range.Size);

                logger.StatusUpdateActivity($"Processing CRC for range {range.Address:X6}-{range.Address + (range.Size - 1):X6}");

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                await this.vehicle.SendToolPresentNotification();

                if (!await this.vehicle.SendMessage(query))
                {
                    this.logger.AddUserMessage($"CRC query failed reading range {range.Address.ToString("X8")} / {range.Size.ToString("X8")}");
                    continue;
                }

                int maxAttempts = 5;
                Message response = await this.vehicle.ReceiveMessage();
                if (response == null)
                {
                    for (int i = 0; i < maxAttempts; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }
                        this.logger.AddDebugMessage($"CRC read failed, re-trying {range.Address.ToString("X8")} / {range.Size.ToString("X8")}");
                        response = await this.vehicle.ReceiveMessage();
                        if (response == null)
                        {
                            continue;
                        }
                        break;
                    }
                }

                Response<UInt32> crcResponse = this.protocol.ParseCrc(response, range.Address, range.Size);
                if (crcResponse.Status != ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Unable to get CRC for memory range " + range.Address.ToString("X8") + " / " + range.Size.ToString("X8"));
                    successForAllRanges = false;
                    continue;
                }

                this.vehicle.ClearDeviceMessageQueue();

                range.ActualCrc = crcResponse.Value;

                this.logger.AddUserMessage(
                    string.Format(
                        formatString,
                        range.Address,
                        range.Address + (range.Size - 1),
                        range.DesiredCrc,
                        range.ActualCrc,
                        range.DesiredCrc == range.ActualCrc ? "Same" : "Different",
                        range.Type));
            }

            await this.vehicle.SendToolPresentNotification();

            foreach (MemoryRange range in this.ranges)
            {
                if ((range.Type & blockTypes) == 0)
                {
                    continue;
                }

                if (range.ActualCrc != range.DesiredCrc)
                {
                    return false;
                }
            }

            this.vehicle.ClearDeviceMessageQueue();

            return successForAllRanges;
        }
    }
}

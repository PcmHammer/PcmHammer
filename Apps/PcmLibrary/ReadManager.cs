// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Contains flash-reading code shared by the WinForms and Uno user interfaces.
    /// </summary>
    public class ReadManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private Func<Action, Task> invoke;
        private Func<Task<string>> promptForFilePath;
        private Func<Task<UInt32>> promptForOperatingSystemId;
        private Func<string, string, Task> alert;
        private Func<string, string, Task<bool>> promptForYesNo;
        private CancellationToken cancellationToken;

        public int CrcPollingDelayMs { get; set; } = 50;

        public ReadManager(
            ILogger logger, 
            Vehicle vehicle,
            Func<Action, Task> invoke, 
            Func<Task<string>> promptForFilePath,
            Func<Task<UInt32>> promptForOperatingSystemId,
            Func<string, string, Task> alert,
            Func<string, string, Task<bool>> promptForYesNo,
            CancellationToken cancellationToken
            )
        {
            this.logger = logger;
            this.vehicle = vehicle;
            this.invoke = invoke;
            this.promptForFilePath = promptForFilePath;
            this.promptForOperatingSystemId = promptForOperatingSystemId;
            this.alert = alert;
            this.promptForYesNo = promptForYesNo;
            this.cancellationToken = cancellationToken;
        }

        public async Task<bool> Read(string path, PcmType forcedPcmType = PcmType.Undefined)
        {
            Response<Stream> readResponse = await RunRead(null, forcedPcmType);
            if (readResponse == null || readResponse.Value == null)
            {
                return false;
            }

            Stream readContents = readResponse.Value;

            if (readResponse.Status == ResponseStatus.Unverified)
            {
                path = GetBadReadPath(path);
                this.logger.AddUserMessage("##############################################################################");
                this.logger.AddUserMessage("WARNING: Verification timed out. File could not be validated and may be corrupt.");
                this.logger.AddUserMessage("Saved to " + path + " for debugging only. Do not use this file without validation.");
                this.logger.AddUserMessage("##############################################################################");
            }

            // Save the contents to the path that the user provided.
            while (true)
            {
                try
                {
                    this.logger.AddUserMessage("Saving contents to " + path);

                    readContents.Position = 0;

                    using (Stream output = File.Open(path, FileMode.Create))
                    {
                        await readContents.CopyToAsync(output);
                    }

                    return true;
                }
                catch (IOException exception)
                {
                    this.logger.AddUserMessage("Unable to save file: " + exception.Message);
                    this.logger.AddDebugMessage(exception.ToString());

                    await this.invoke(async () => path = await this.promptForFilePath());
                    if (path == null)
                    {
                        this.logger.AddUserMessage("Save canceled.");

                        // Returning true to indicate that the read worked. It doesn't
                        // really matter that the user chose not to keep the file.
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to read the PCM's flash memory.
        /// </summary>
        /// <returns>The stream on success or unverified read; null on failure or abort.</returns>
        public async Task<Stream?> Read(IProgress<ProgressUpdate>? progress = null, PcmType forcedPcmType = PcmType.Undefined)
        {
            Response<Stream> readResponse = await RunRead(progress, forcedPcmType);
            if (readResponse == null || readResponse.Value == null)
            {
                return null;
            }
            if (readResponse.Status == ResponseStatus.Success || readResponse.Status == ResponseStatus.Unverified)
            {
                return readResponse.Value;
            }
            return null;
        }

        private static string GetBadReadPath(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            return Path.Combine(dir, name + "_badread" + ext);
        }

        private async Task<Response<Stream>> RunRead(IProgress<ProgressUpdate>? progress, PcmType forcedPcmType)
        {
            OSIDInfo pcmInfo;
            if (forcedPcmType != PcmType.Undefined)
            {
                pcmInfo = new OSIDInfo(forcedPcmType);
                this.logger.AddUserMessage("Using manually selected PCM type: " + pcmInfo.HardwareType);
            }
            else
            {
                this.logger.AddUserMessage("Querying operating system of current PCM.");
                Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                if (osidResponse.Status != ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("Operating system query failed, will retry: " + osidResponse.Status);
                    await this.vehicle.ExitKernel();

                    osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                    if (osidResponse.Status != ResponseStatus.Success)
                    {
                        this.logger.AddUserMessage("Operating system query failed: " + osidResponse.Status);
                    }
                }

                if (osidResponse.Status == ResponseStatus.Success)
                {
                    this.logger.AddUserMessage("OSID: " + osidResponse.Value);
                    pcmInfo = new OSIDInfo(osidResponse.Value);
                    this.logger.AddUserMessage("Description: " + pcmInfo.Description);
                }
                else
                {
                    this.logger.AddUserMessage("Unable to get operating system ID. Will assume this can be unlocked with the default seed/key algorithm.");

                    UInt32 OperatingSystemId = 0;

                    await this.vehicle.ForceSendToolPresentNotification();
                    await this.invoke(async () => OperatingSystemId = await this.promptForOperatingSystemId());
                    await this.vehicle.ForceSendToolPresentNotification();

                    pcmInfo = new OSIDInfo(OperatingSystemId);

                    this.logger.AddUserMessage($"Using OsID: {pcmInfo.OSID}");
                }
            }

            if (!pcmInfo.IsSupported)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                this.logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            if (!pcmInfo.IsSupportedRead)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                this.logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            if (pcmInfo.HardwareType == PcmType.P05 || pcmInfo.HardwareType == PcmType.P05b)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                this.logger.AddUserMessage(msg);
                bool shouldContinue = false;
                await this.invoke(async () => { shouldContinue = await this.promptForYesNo(msg, "Continue?"); });
                if (!shouldContinue)
                {
                    this.logger.AddUserMessage("User chose not to proceed.");
                    return null;
                }
            }

            await this.vehicle.SuppressChatter();

            bool unlocked = await this.vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
            if (!unlocked)
            {
                this.logger.AddUserMessage("Unlock was not successful.");
                return null;
            }

            this.logger.AddUserMessage("Unlock succeeded.");

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            DateTime start = DateTime.Now;

            CKernelReader reader = new CKernelReader(
                this.vehicle,
                pcmInfo,
                this.logger)
            {
                CrcPollingDelayMs = this.CrcPollingDelayMs,
            };

            Response<Stream> readResponse = await reader.ReadContents(this.cancellationToken, progress);

            this.logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

            if (readResponse.Status != ResponseStatus.Success && readResponse.Status != ResponseStatus.Unverified)
            {
                this.logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                return null;
            }

            return readResponse;
        }
    }
}

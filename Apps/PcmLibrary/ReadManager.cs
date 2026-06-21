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
        private Func<Task<string?>> promptForFilePath;
        private Func<Task<PcmType>> promptForPcmType;
        private Func<string, string, Task> alert;
        private Func<string, string, Task<bool>> promptForYesNo;
        private CancellationToken cancellationToken;

        public int CrcPollingDelayMs { get; set; } = 50;

        public ReadManager(
            ILogger logger, 
            Vehicle vehicle,
            Func<Action, Task> invoke, 
            Func<Task<string?>> promptForFilePath,
            Func<Task<PcmType>> promptForPcmType,
            Func<string, string, Task> alert,
            Func<string, string, Task<bool>> promptForYesNo,
            CancellationToken cancellationToken
            )
        {
            this.logger = logger;
            this.vehicle = vehicle;
            this.invoke = invoke;
            this.promptForFilePath = promptForFilePath;
            this.promptForPcmType = promptForPcmType;
            this.alert = alert;
            this.promptForYesNo = promptForYesNo;
            this.cancellationToken = cancellationToken;
        }

        public async Task<bool> Read(string path, PcmType forcedPcmType = PcmType.Undefined)
        {
            Response<Stream>? readResponse = await RunRead(null, forcedPcmType);
            if (readResponse == null || readResponse.Value == null)
            {
                return false;
            }

            Stream readContents = readResponse.Value;

            if (readResponse.Status == ResponseStatus.Unverified)
            {
                path = GetBadReadPath(path);
                logger.AddUserMessage("##############################################################################");
                logger.AddUserMessage("WARNING: Verification timed out. File could not be validated and may be corrupt.");
                logger.AddUserMessage("Saved to " + path + " for debugging only. Do not use this file without validation.");
                logger.AddUserMessage("##############################################################################");
            }

            // Save the contents to the path that the user provided.
            while (true)
            {
                try
                {
                    logger.AddUserMessage("Saving contents to " + path);

                    readContents.Position = 0;

                    using (Stream output = File.Open(path, FileMode.Create))
                    {
                        await readContents.CopyToAsync(output);
                    }

                    return true;
                }
                catch (IOException exception)
                {
                    logger.AddUserMessage("Unable to save file: " + exception.Message);
                    logger.AddDebugMessage(exception.ToString());

                    string? newPath = null;
                    await this.invoke(async () => newPath = await this.promptForFilePath());
                    if (newPath == null)
                    {
                        logger.AddUserMessage("Save canceled.");

                        // Returning true to indicate that the read worked. It doesn't
                        // really matter that the user chose not to keep the file.
                        return true;
                    }
                    path = newPath;
                }
            }
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to read the PCM's flash memory.
        /// </summary>
        /// <returns>The stream on success or unverified read; null on failure or abort.</returns>
        public async Task<Stream?> Read(IProgress<ProgressUpdate>? progress = null, PcmType forcedPcmType = PcmType.Undefined)
        {
            Response<Stream>? readResponse = await RunRead(progress, forcedPcmType);
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

        /// <summary>
        /// Read a CAN-bus PCM (E38). The VPW OSID table doesn't describe these, so we use the E38
        /// profile for the key algorithm, kernel file and image size. Mirrors the VPW path: unlock,
        /// then hand off to the CAN kernel reader for the upload + block read. Assumes the device is
        /// already selected on CAN.
        /// </summary>
        private async Task<Response<Stream>?> RunCanRead(IProgress<ProgressUpdate>? progress)
        {
            OSIDInfo pcmInfo = new OSIDInfo(PcmType.E38);
            logger.AddDebugMessage("CAN PCM detected. Using the " + pcmInfo.Description + " read process.");

            if (!pcmInfo.IsSupportedRead)
            {
                string msg = "Abort: this CAN PCM is not supported for read operations.";
                logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            CanCommands commands = this.vehicle.CreateCanCommands();

            logger.StatusUpdateActivity("Unlocking PCM...");
            if (!await commands.Unlock(pcmInfo, this.cancellationToken))
            {
                logger.AddUserMessage("Unlock was not successful.");
                return null;
            }
            logger.AddUserMessage("Unlock succeeded.");

            if (this.cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            DateTime start = DateTime.Now;
            CanKernelReader reader = new CanKernelReader(this.vehicle, commands, pcmInfo, this.logger);
            Response<Stream> readResponse = await reader.ReadContents(this.cancellationToken, progress);

            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

            if (readResponse.Status != ResponseStatus.Success && readResponse.Status != ResponseStatus.Unverified)
            {
                logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                return null;
            }

            return readResponse;
        }

        private async Task<Response<Stream>?> RunRead(IProgress<ProgressUpdate>? progress, PcmType forcedPcmType)
        {
            OSIDInfo pcmInfo;
            if (forcedPcmType != PcmType.Undefined)
            {
                pcmInfo = new OSIDInfo(forcedPcmType);
                logger.AddDebugMessage("Using manually selected PCM type: " + pcmInfo.HardwareType);
            }
            else
            {
                // Detect what is on the bus first (the shared first step). A CAN PCM is read by a
                // separate path; otherwise make sure we are on VPW and use the VPW flow below.
                DetectedModule? detected = await this.vehicle.DetectAndSelectPcm(this.cancellationToken);
                if (detected != null && detected.Bus == BusProtocol.Can500k)
                {
                    return await this.RunCanRead(progress);
                }
                if (detected == null)
                {
                    // The quick probe found nothing; the device may have been left on CAN, so return
                    // it to VPW for the full VPW detection below (which has its own retries).
                    await this.vehicle.SelectBus(BusProtocol.Vpw);
                }

                logger.AddUserMessage("Querying operating system of current PCM.");
                Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                if (osidResponse.Status != ResponseStatus.Success)
                {
                    logger.AddUserMessage("Operating system query failed, will retry: " + osidResponse.Status);
                    await this.vehicle.ExitKernel();

                    osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                    if (osidResponse.Status != ResponseStatus.Success)
                    {
                        logger.AddUserMessage("Operating system query failed: " + osidResponse.Status);
                    }
                }

                if (osidResponse.Status == ResponseStatus.Success)
                {
                    logger.AddUserMessage("OSID: " + osidResponse.Value);
                    pcmInfo = new OSIDInfo(osidResponse.Value);
                    logger.AddUserMessage("Description: " + pcmInfo.Description);
                }
                else
                {
                    logger.AddUserMessage("Unable to get operating system ID. Asking the user to select the PCM type.");

                    PcmType selectedType = PcmType.Undefined;

                    await this.vehicle.ForceSendToolPresentNotification();
                    await this.invoke(async () => selectedType = await this.promptForPcmType());
                    await this.vehicle.ForceSendToolPresentNotification();

                    if (selectedType == PcmType.Undefined)
                    {
                        logger.AddUserMessage("No PCM type was selected.");
                        return null;
                    }

                    pcmInfo = new OSIDInfo(selectedType);

                    logger.AddUserMessage($"Using manually selected PCM type: {pcmInfo.HardwareType}");
                }
            }

            // The forced-type path (normally the type detected at form load) skips the detection
            // branch that routes CAN to its own path, so dispatch here too (mirrors WriteManager). Put
            // the device on CAN and point it at the PCM first, else the VPW unlock/kernel flow runs
            // over the CAN bus and no seed is parsed.
            if (pcmInfo.BusProtocol == BusProtocol.Can500k)
            {
                this.vehicle.SetTarget(Target.Pcm);
                if (!await this.vehicle.SelectBus(BusProtocol.Can500k))
                {
                    string msg = $"Abort: this device cannot use the CAN bus required by the {pcmInfo.HardwareType} PCM.";
                    logger.AddUserMessage(msg);
                    await this.invoke(async () => await this.alert(msg, "Abort"));
                    return null;
                }

                return await this.RunCanRead(progress);
            }

            if (!pcmInfo.IsSupported)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            if (!pcmInfo.IsSupportedRead)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            if (pcmInfo.HardwareType == PcmType.P05 || pcmInfo.HardwareType == PcmType.P05b)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                logger.AddUserMessage(msg);
                bool shouldContinue = false;
                await this.invoke(async () => { shouldContinue = await this.promptForYesNo(msg, "Continue?"); });
                if (!shouldContinue)
                {
                    logger.AddUserMessage("User chose not to proceed.");
                    return null;
                }
            }

            await this.vehicle.SuppressChatter();

            bool unlocked = await this.vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
            if (!unlocked)
            {
                logger.AddUserMessage("Unlock was not successful.");
                return null;
            }

            logger.AddUserMessage("Unlock succeeded.");

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

            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

            if (readResponse.Status != ResponseStatus.Success && readResponse.Status != ResponseStatus.Unverified)
            {
                logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                return null;
            }

            return readResponse;
        }
    }
}

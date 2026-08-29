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

        // Set for the duration of a RecoveryRead. A PCM in recovery sits in its boot loader with no
        // operating system, so security access and the 4X switch become best-effort: failing them is
        // expected and must not stop the read.
        private bool isRecovery;

        // Slave modules identified by part number. A slave cannot be read, so the package records a
        // reference to each one rather than its bytes.
        private readonly List<PackageImage> capturedSlaveReferences = new List<PackageImage>();

        // Stateless message factory, for parsing DID responses.
        private static readonly Gmlan gmlan = new Gmlan();

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

            // Wrap the image so the save dialog's extension picks the format: a raw .bin, or a .phz with
            // a manifest and integrity checksums.
            readContents.Position = 0;
            byte[] image;
            using (MemoryStream buffer = new MemoryStream())
            {
                await readContents.CopyToAsync(buffer);
                image = buffer.ToArray();
            }

            PcmPackage package = BuildPackage(image, forcedPcmType);

            while (true)
            {
                try
                {
                    logger.AddUserMessage("Saving contents to " + path);
                    PackageStore.Save(path, package);
                    return true;
                }
                catch (Exception exception) when (exception is IOException || exception is PackageException)
                {
                    logger.AddUserMessage("Unable to save file: " + exception.Message);
                    logger.AddDebugMessage(exception.ToString());

                    string? newPath = null;
                    await this.invoke(async () => newPath = await this.promptForFilePath());
                    if (newPath == null)
                    {
                        // The read worked; the user just chose not to keep the file.
                        logger.AddUserMessage("Save canceled.");
                        return true;
                    }
                    path = newPath;
                }
            }
        }

        /// <summary>
        /// Read the PCM's main flash and return it as an in-memory package, without saving. The UI holds
        /// this as its working document and saves it separately. Null on failure or abort; an unverified
        /// read still returns the package (the caller is warned) so it can be saved for debugging.
        /// </summary>
        public Task<PcmPackage?> ReadToPackage(PcmType forcedPcmType = PcmType.Undefined) =>
            this.ReadToPackage(null, forcedPcmType);

        /// <summary>
        /// Read a PCM that is in recovery mode into an in-memory package. The PCM type is supplied by
        /// the user because a PCM in recovery reports no operating system; detection, the OSID query and
        /// the kernel probe are all skipped. See <see cref="RecoveryMode"/>. Null on failure or abort.
        /// </summary>
        public async Task<PcmPackage?> RecoveryRead(PcmType pcmType, IProgress<ProgressUpdate>? progress = null)
        {
            if (!RecoveryMode.CanAttempt(pcmType, out string reason))
            {
                logger.AddUserMessage(reason);
                await this.invoke(async () => await this.alert(reason, "PCM Recovery"));
                return null;
            }

            logger.AddUserMessage(RecoveryMode.DescribeEntry(pcmType, isWrite: false));

            // Advisory only: not every interface can see the broadcast, so a negative result must
            // never stop a recovery attempt.
            Response<bool> broadcasting = await this.vehicle.CheckForRecoveryMode(this.cancellationToken);
            logger.AddUserMessage(broadcasting.Status == ResponseStatus.Success && broadcasting.Value
                ? "The PCM is broadcasting a programming request, so it is waiting to be programmed."
                : "No programming request seen. Continuing anyway - not every interface can detect one.");

            // Forcing the type is what takes us straight into the recovery flow.
            this.isRecovery = true;
            try
            {
                return await this.ReadToPackage(progress, pcmType);
            }
            finally
            {
                this.isRecovery = false;
            }
        }

        /// <summary>As <see cref="ReadToPackage(PcmType)"/>, reporting progress during the read.</summary>
        public async Task<PcmPackage?> ReadToPackage(IProgress<ProgressUpdate>? progress, PcmType forcedPcmType = PcmType.Undefined)
        {
            Response<Stream>? readResponse = await RunRead(progress, forcedPcmType);
            if (readResponse == null || readResponse.Value == null)
            {
                return null;
            }

            if (readResponse.Status == ResponseStatus.Unverified)
            {
                logger.AddUserMessage("##############################################################################");
                logger.AddUserMessage("WARNING: Verification timed out. The image could not be validated and may be corrupt.");
                logger.AddUserMessage("Save it for debugging only. Do not write this file to a PCM without validation.");
                logger.AddUserMessage("##############################################################################");
            }

            Stream readContents = readResponse.Value;
            readContents.Position = 0;
            byte[] image;
            using (MemoryStream buffer = new MemoryStream())
            {
                await readContents.CopyToAsync(buffer);
                image = buffer.ToArray();
            }

            return BuildPackage(image, forcedPcmType);
        }

        /// <summary>
        /// Wrap a freshly-read main image in a package. Module type and OSID are identified from the image
        /// (best-effort) for the .phz manifest; a raw .bin save ignores them and writes the bytes verbatim.
        /// </summary>
        private PcmPackage BuildPackage(byte[] image, PcmType forcedPcmType)
        {
            string? moduleType = null;
            uint? osid = null;
            try
            {
                FileValidator validator = new FileValidator(
                    image, this.logger, forcedPcmType != PcmType.Undefined ? forcedPcmType : (PcmType?)null);

                PcmType type = forcedPcmType != PcmType.Undefined ? forcedPcmType : validator.DetectFileType();
                if (type != PcmType.Undefined)
                {
                    moduleType = type.ToString();
                }

                uint id = validator.GetOsidFromImage();
                if (id != 0)
                {
                    osid = id;
                }
            }
            catch
            {
                // Identification is best-effort metadata; the save works without it.
            }

            var controller = new PackageController
            {
                Id = 1,
                Type = "PCM",
                ModuleType = moduleType,
                Images = { new PackageImage { Target = "main", FileName = "main.bin", Data = image, Osid = osid } }
            };

            // Slave modules identified before the read as references (part number, no bytes - the writer
            // resolves them from the local library). Empty for PCMs without a slave.
            foreach (PackageImage slaveReference in this.capturedSlaveReferences)
            {
                controller.Images.Add(slaveReference);
            }

            return new PcmPackage
            {
                Generator = "PcmHammer",
                Created = DateTime.UtcNow.ToString("o"),
                Controllers = { controller }
            };
        }

        /// <summary>
        /// Query the PCM (in normal mode) for the part number of each slave module it declares and stash
        /// them as package references. A no-op for PCMs without a slave. See <see cref="OSIDInfo.SlaveModules"/>.
        /// </summary>
        private async Task CaptureSlaveReferences(CanCommands commands, OSIDInfo pcmInfo)
        {
            this.capturedSlaveReferences.Clear();
            if (!pcmInfo.HardwareSlaveCPU || pcmInfo.SlaveModules.Count == 0)
            {
                return;
            }

            foreach (SlaveModuleId module in pcmInfo.SlaveModules)
            {
                if (this.cancellationToken.IsCancellationRequested) break;

                Response<byte[]> response = await commands.ReadDataByIdentifier(module.Did, this.cancellationToken);
                Response<uint> partNumber = response.Status == ResponseStatus.Success
                    ? gmlan.ParseReadByIdUInt32(new Message(response.Value), module.Did)
                    : Response.Create(ResponseStatus.Error, 0u);

                // A zero or 0xFFFFFFFF part number is an empty / unprogrammed slot; don't record it.
                if (partNumber.Status == ResponseStatus.Success && partNumber.Value != 0 && partNumber.Value != 0xFFFFFFFF)
                {
                    this.capturedSlaveReferences.Add(
                        PackageImage.Reference(module.Target, partNumber.Value + ".bin", partNumber.Value));
                    logger.AddUserMessage(string.Format("Slave module {0}: {1}", module.Target, partNumber.Value));
                }
                else
                {
                    logger.AddDebugMessage(string.Format(
                        "Slave module {0} (DID 0x{1:X2}) did not report a part number; it will not be recorded.",
                        module.Target, module.Did));
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
        /// Read a CAN-bus PCM. The caller supplies the PCM profile (key algorithm, kernel file, base
        /// and image size): the forced-type path passes the selected PCM, the auto-detect path passes
        /// the detected one. Mirrors the VPW path: unlock, then hand off to the CAN kernel reader for
        /// the upload + block read. Assumes the device is already selected on CAN.
        /// </summary>
        private async Task<Response<Stream>?> RunCanRead(IProgress<ProgressUpdate>? progress, OSIDInfo pcmInfo)
        {
            logger.AddDebugMessage("CAN PCM detected. Using the " + pcmInfo.Description + " read process.");

            if (!pcmInfo.IsSupportedRead)
            {
                string msg = "Abort: this CAN PCM is not supported for read operations.";
                logger.AddUserMessage(msg);
                await this.invoke(async () => await this.alert(msg, "Abort"));
                return null;
            }

            CanCommands commands = this.vehicle.CreateCanCommands();

            // Identify the slave modules while the PCM is still in normal mode, before the read kernel
            // takes over.
            await this.CaptureSlaveReferences(commands, pcmInfo);

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
            CanKernelSession session = new CanKernelSession(this.vehicle, commands, this.logger);
            KernelReader reader = new KernelReader(session, pcmInfo, this.logger);
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
                    // Auto-detect on CAN: resolve the PCM from its OSID
                    OSIDInfo detectedInfo = new OSIDInfo(detected.Osid);
                    return await this.RunCanRead(progress, detectedInfo);
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

            // Select the bus once the PCM type is known, whichever branch above resolved it. Every
            // route ends here, so a CAN PCM cannot reach the VPW flow below. WriteManager does the
            // same with the same helper.
            switch (await this.vehicle.PrepareBusFor(pcmInfo))
            {
                case BusPreparation.Unavailable:
                    string msg = $"Abort: this device cannot use the CAN bus required by the {pcmInfo.HardwareType} PCM.";
                    logger.AddUserMessage(msg);
                    await this.invoke(async () => await this.alert(msg, "Abort"));
                    return null;

                case BusPreparation.Ready:
                    return await this.RunCanRead(progress, pcmInfo);
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

            bool unlocked = await this.vehicle.UnlockEcu(pcmInfo.KeyAlgorithm, this.cancellationToken);
            if (!unlocked)
            {
                logger.AddUserMessage("Unlock was not successful.");

                // A PCM in recovery has no operating system to run security access, so a refusal here
                // says nothing about whether it can be read. Carry on and let the boot loader answer
                // for itself.
                if (!this.isRecovery || this.cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                logger.AddUserMessage("Recovery: continuing without security access.");
            }
            else
            {
                logger.AddUserMessage("Unlock succeeded.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            DateTime start = DateTime.Now;

            VpwKernelSession session = new VpwKernelSession(this.vehicle, this.logger)
            {
                IsRecovery = this.isRecovery,
                CrcPollingDelayMs = this.CrcPollingDelayMs,
            };

            KernelReader reader = new KernelReader(session, pcmInfo, this.logger);
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

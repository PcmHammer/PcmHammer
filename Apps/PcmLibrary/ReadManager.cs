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

            // Also settles which bus to work over. Advisory only: not every interface can see the
            // request, so a negative result must never stop a recovery attempt - it just leaves the
            // bus as the selected PCM type implies.
            ProgrammingRequest? request = await this.vehicle.FindProgrammingRequest(
                new OSIDInfo(pcmType).BusProtocol, this.cancellationToken);
            logger.AddUserMessage(RecoveryMode.DescribeProgrammingRequest(request));

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

        /// <summary>
        /// Read an arbitrary flash range - including below <see cref="OSIDInfo.ReadStartAddress"/>, e.g.
        /// the E92 protected boot block - and save the raw bytes. Tolerant of sectors that fault the
        /// read kernel: a block that fails to read is filled with 0xFF, the rest of its 16 KiB sector is
        /// skipped, the kernel is re-established (the fault may have reset it), and the read continues,
        /// so everything readable is still captured. CAN PCMs only.
        /// </summary>
        public async Task<bool> ReadRawRange(uint startAddress, uint length, string filePath, PcmType forcedPcmType = PcmType.Undefined)
        {
            OSIDInfo pcmInfo;
            if (forcedPcmType != PcmType.Undefined)
            {
                pcmInfo = new OSIDInfo(forcedPcmType);
            }
            else
            {
                DetectedModule? detected = await this.vehicle.DetectAndSelectPcm(this.cancellationToken);
                if (detected == null || detected.Bus != BusProtocol.Can500k)
                {
                    logger.AddUserMessage("Raw range read is only supported for CAN PCMs.");
                    return false;
                }

                pcmInfo = detected.Info;
            }

            if (!await this.vehicle.SelectBus(pcmInfo.BusProtocol))
            {
                logger.AddUserMessage("Failed to select the " + pcmInfo.BusProtocol + " bus.");
                return false;
            }

            logger.AddUserMessage(string.Format(
                "Raw range read: 0x{0:X6}-0x{1:X6} on {2}.", startAddress, startAddress + length, pcmInfo.Description));

            CanCommands commands = this.vehicle.CreateCanCommands();
            CanKernelSession session = new CanKernelSession(this.vehicle, commands, this.logger);
            if (!await this.StartRawKernel(pcmInfo, session, firstStart: true))
            {
                return false;
            }

            byte[] buffer = new byte[length];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xFF;
            }

            const uint SectorSize = 0x4000;   // smallest E92 flash sector; the skip granularity on a fault
            uint blockSize = (uint)session.MaxReadBlockSize;
            var skipped = new List<uint>();
            DateTime start = DateTime.Now;
            uint offset = 0;

            while (offset < length)
            {
                if (this.cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                uint address = startAddress + offset;
                int thisBlock = (int)Math.Min(blockSize, length - offset);

                await session.KeepAlive(this.cancellationToken);
                Response<byte[]> block = await session.ReadMemoryBlock(address, thisBlock, this.cancellationToken);

                if (block.Status == ResponseStatus.Success && block.Value.Length == thisBlock)
                {
                    Buffer.BlockCopy(block.Value, 0, buffer, (int)offset, thisBlock);
                    logger.StatusUpdateProgressBar((double)(offset + (uint)thisBlock) / length, true);
                    offset += (uint)thisBlock;
                    continue;
                }

                // The block did not read - most likely it faulted and reset the kernel. Skip the rest of
                // its 16 KiB sector (left 0xFF), re-establish the kernel, and press on.
                uint nextSector = ((address / SectorSize) + 1) * SectorSize;
                logger.AddUserMessage(string.Format(
                    "Block 0x{0:X6} did not read ({1}); skipping to 0x{2:X6} and re-establishing the kernel.",
                    address, block.Status, nextSector));
                skipped.Add(address);

                if (!await this.StartRawKernel(pcmInfo, session, firstStart: false))
                {
                    logger.AddUserMessage("Could not re-establish the kernel; stopping the range read.");
                    break;
                }

                offset = nextSector - startAddress;
            }

            await commands.Reboot(this.cancellationToken);

            try
            {
                File.WriteAllBytes(filePath, buffer);
            }
            catch (Exception exception)
            {
                logger.AddUserMessage("Could not save the range: " + exception.Message);
                return false;
            }

            logger.AddUserMessage(string.Format(
                "Saved 0x{0:X} bytes to {1}. {2} block(s)/sector(s) could not be read.", length, filePath, skipped.Count));
            foreach (uint address in skipped)
            {
                logger.AddUserMessage(string.Format("  unreadable near 0x{0:X6}", address));
            }
            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            return true;
        }

        // Upload the read kernel. The upload's EnterProgrammingMode unlocks in-session, so there is no
        // separate pre-session unlock here. When re-establishing after a suspected fault reset, give the
        // stock OS a moment to come back first.
        private async Task<bool> StartRawKernel(OSIDInfo pcmInfo, CanKernelSession session, bool firstStart)
        {
            if (!firstStart)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), this.cancellationToken);
            }

            return await session.Start(pcmInfo, KernelOperation.Read, kernelAlreadyRunning: false, this.cancellationToken);
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

            // No pre-session unlock: the kernel upload's EnterProgrammingMode unlocks in-session (security
            // access is per programming session). Unlocking here as well just re-runs 0x27, and the
            // in-session attempt then reports "already unlocked" (seed 0x0000).
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
                    return await this.RunCanRead(progress, detected.Info);
                }
                if (detected == null)
                {
                    // The quick probe found nothing; the device may have been left on CAN, so return
                    // it to VPW for the full VPW detection below (which has its own retries).
                    await this.vehicle.SelectBus(BusProtocol.VPW);
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

            VPWKernelSession session = new VPWKernelSession(this.vehicle, this.logger)
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

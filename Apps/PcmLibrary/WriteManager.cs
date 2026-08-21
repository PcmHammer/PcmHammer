// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Contains flash-writing code shared by the WinForms and Uno user interfaces.
    /// </summary>
    public class WriteManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private WriteType writeType;
        private Func<string, string, Task> alert;
        private Func<string, string, Task<bool>> promptForYesNo;
        private CancellationToken cancellationToken;

        public WriteManager(
            ILogger logger,
            Vehicle vehicle,
            WriteType writeType,
            Func<string, string, Task> alert,
            Func<string, string, Task<bool>> promptForYesNo,
            CancellationToken cancellationToken)
        {
            this.logger = logger;
            this.vehicle = vehicle;
            this.writeType = writeType;
            this.alert = alert;
            this.promptForYesNo = promptForYesNo;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Load a file (a raw .bin or a .phz package) and write it. Accepts a string path for OSes that
        /// can directly access the file system.
        /// </summary>
        public async Task<bool> Write(string path, PcmType forcedPcmType = PcmType.Undefined)
        {
            PcmPackage package;
            try
            {
                // A .bin loads as a single-controller package with one "main" image; a .phz may also
                // carry slave references.
                package = PackageStore.Load(path);
            }
            catch (PackageException exception)
            {
                logger.AddUserMessage("Unable to load file: " + exception.Message);
                return false;
            }

            return await this.Write(package, forcedPcmType);
        }

        /// <summary>
        /// Write an already-loaded package (the UI's in-memory working document). Slave handling is
        /// derived, not chosen: if the package carries slave modules and the PCM supports a boot loader
        /// write, a full write programs the whole PCM through the boot loader; otherwise the master image
        /// is written the normal way and any slave in the PCM is left in place.
        /// </summary>
        /// <summary>
        /// Write to a PCM that is in recovery mode. The PCM type is supplied by the user because a PCM
        /// in recovery reports no operating system; detection, the OSID query and the kernel probe are
        /// all skipped. See <see cref="RecoveryMode"/>.
        /// </summary>
        /// <remarks>
        /// The boot-sector protection still applies in full: once the kernel is running it CRCs the
        /// real flash, and <see cref="WritePlan.BootPolicyAllowsWritePlan"/> aborts before any erase if
        /// the plan would write a boot sector this PCM cannot rewrite. That is what stops a recoverable
        /// soft brick from becoming a hard brick.
        /// </remarks>
        public async Task<bool> RecoveryWrite(PcmPackage package, PcmType pcmType)
        {
            if (!RecoveryMode.CanAttempt(pcmType, out string reason))
            {
                logger.AddUserMessage(reason);
                await this.alert(reason, "PCM Recovery");
                return false;
            }

            logger.AddUserMessage(RecoveryMode.DescribeEntry(pcmType, isWrite: true));

            // Forcing the type is what takes us straight into the recovery flow.
            return await this.Write(package, pcmType);
        }

        public async Task<bool> Write(PcmPackage package, PcmType forcedPcmType = PcmType.Undefined)
        {
            PackageImage? main = SelectMainImage(package);
            if (main?.Data == null)
            {
                logger.AddUserMessage("This file has no main image to write.");
                return false;
            }

            PackageController controller = package.Controllers.First(c => c.Images.Contains(main));
            List<PackageImage> slaveImages = controller.Images
                .Where(i => IsSlaveTarget(i.Target))
                .OrderBy(i => SlaveOrder(i.Target))
                .ToList();

            // The master and slave are written together only for a full write; compare, test write and
            // partial writes act on the master alone and fall through below.
            if (slaveImages.Count > 0 && this.writeType == WriteType.Full)
            {
                OSIDInfo? pcmInfo = forcedPcmType != PcmType.Undefined
                    ? new OSIDInfo(forcedPcmType)
                    : PackageCompleteness.ResolvePlatform(controller);

                if (pcmInfo != null && pcmInfo.IsSupportedBootLoaderWrite)
                {
                    // A reference carries no bytes, so resolve it from the local library.
                    var slaveModules = new List<byte[]>();
                    var missing = new List<string>();
                    foreach (PackageImage slave in slaveImages)
                    {
                        byte[]? data = slave.Data ?? SlaveLibrary.Resolve(slave.FileName);
                        if (data == null)
                        {
                            missing.Add(slave.FileName ?? slave.Target ?? "?");
                        }
                        else
                        {
                            logger.AddUserMessage(string.Format(
                                "Slave {0} ({1}): resolved from the local library.", slave.Target, slave.FileName));
                            slaveModules.Add(data);
                        }
                    }

                    if (missing.Count == 0)
                    {
                        return await this.WriteMasterAndSlave(main.Data, slaveModules, pcmInfo);
                    }

                    // This file includes the slave, so do not quietly downgrade to a master-only write.
                    logger.AddUserMessage("=================================================================");
                    logger.AddUserMessage("SLAVE NOT WRITTEN - this file includes the slave CPU, but these");
                    logger.AddUserMessage("slave module(s) are missing from the local library:");
                    logger.AddUserMessage("  " + SlaveLibrary.DefaultDirectory);
                    foreach (string moduleName in missing)
                    {
                        logger.AddUserMessage("    " + moduleName);
                    }
                    logger.AddUserMessage("Add the matching module file(s) there and try again.");
                    logger.AddUserMessage("The PCM was NOT written.");
                    logger.AddUserMessage("=================================================================");
                    return false;
                }

                logger.AddUserMessage(
                    "This file has slave modules, but this PCM has no boot loader write path. Writing the master only.");
            }

            return await Write(main.Data, forcedPcmType);
        }

        private static bool IsSlaveTarget(string? target) =>
            target != null && target.StartsWith("slave", StringComparison.OrdinalIgnoreCase);

        // Stream order the boot loader expects: the slave OS module before the slave calibration.
        private static int SlaveOrder(string? target) =>
            string.Equals(target, "slave-os", StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(target, "slave-calibration", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

        private PackageImage? SelectMainImage(PcmPackage package)
        {
            var candidates = package.Controllers.Where(c => c.Image("main") != null).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count > 1)
            {
                PackageController first = candidates[0];
                logger.AddUserMessage(string.Format(
                    "This package has {0} controllers with a main image; writing the first ({1}). " +
                    "Controller selection is not available yet.",
                    candidates.Count, first.ModuleType ?? first.Type ?? ("id " + first.Id)));
            }

            return candidates[0].Image("main");
        }

        /// <summary>
        /// Write the master and the slave through the resident boot loader in one download. The master
        /// image is split into flash modules by <see cref="FlashModuleBuilder"/>; streaming the master OS
        /// module arms the slave, then the slave modules are programmed. This can brick the PCM, so it is
        /// gated on a confirmation.
        /// </summary>
        private async Task<bool> WriteMasterAndSlave(byte[] masterImage, IList<byte[]> slaveModules, OSIDInfo pcmInfo)
        {
            // Checksum-validate the master first, the same gate as a normal write: a bad sum means the
            // image is corrupt and would render the PCM unusable.
            FileValidator validator = new FileValidator(masterImage, this.logger, pcmInfo.HardwareType);
            if (!validator.IdentifyAndValidate())
            {
                logger.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                return false;
            }

            logger.AddUserMessage("File OSID: " + validator.GetOsidFromImage());
            logger.AddUserMessage("File Description: " + new OSIDInfo(validator.GetFileType()).Description + ".");

            logger.AddUserMessage(
                "Programming the FULL PCM - the master flash AND the slave CPU - through the boot loader.");

            List<byte[]> masterModules;
            try
            {
                masterModules = FlashModuleBuilder.Build(masterImage, pcmInfo);
            }
            catch (Exception exception)
            {
                logger.AddUserMessage("Could not split the master image into modules: " + exception.Message);
                return false;
            }

            byte[]? masterFlashLibrary = SlaveLibrary.Resolve(pcmInfo.BootLoaderMasterLibraryFileName);
            byte[]? slaveFlashDriver = SlaveLibrary.Resolve(pcmInfo.BootLoaderSlaveDriverFileName);
            if (masterFlashLibrary == null || slaveFlashDriver == null)
            {
                logger.AddUserMessage("Missing the boot loader flash routines in the local library:");
                logger.AddUserMessage("  " + SlaveLibrary.DefaultDirectory);
                if (masterFlashLibrary == null)
                {
                    logger.AddUserMessage("    " + pcmInfo.BootLoaderMasterLibraryFileName);
                }

                if (slaveFlashDriver == null)
                {
                    logger.AddUserMessage("    " + pcmInfo.BootLoaderSlaveDriverFileName);
                }

                return false;
            }

            if (!await this.vehicle.SelectBus(pcmInfo.BusProtocol))
            {
                logger.AddUserMessage("Failed to select the " + pcmInfo.BusProtocol + " bus.");
                return false;
            }

            DateTime start = DateTime.Now;
            CanCommands commands = this.vehicle.CreateCanCommands();
            CanBootLoaderWriter writer = new CanBootLoaderWriter(this.vehicle, commands, pcmInfo, this.logger);
            bool success = await writer.Write(
                masterFlashLibrary, masterModules, slaveFlashDriver, slaveModules, this.cancellationToken);
            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

            if (success)
            {
                logger.AddUserMessage("Verify the result with a PCM identification.");
                return true;
            }

            logger.AddUserMessage("The write did not complete. Review the log.");
            return false;
        }

        /// <summary>
        /// Write a CAN-bus PCM. Mirrors the VPW write process but goes through the CAN kernel
        /// writer: identify, run the brick-risk gates, unlock, then hand off to <see cref="CanKernelWriter"/>
        /// for the upload + compare/erase/write/verify loop. Assumes the device is already selected on
        /// CAN. A test write is non-destructive and is always allowed.
        /// </summary>
        private async Task<bool> RunCanWrite(byte[] image, FileValidator validator)
        {
            // Use the type identified from the file (P05c, E38, ...)
            OSIDInfo pcmInfo = new OSIDInfo(validator.GetFileType());

            string operation =
                this.writeType == WriteType.Compare ? "verify" :
                this.writeType == WriteType.TestWrite ? "test write" :
                "write";
            logger.AddUserMessage($"CAN PCM detected. Using the {pcmInfo.Description} {operation} process.");

            if (!pcmInfo.IsSupported)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Abort");
                return false;
            }

            // Comparisons and test writes are non-destructive, so they are allowed even where a real
            // write is not. A real write is gated on the PCM's write support like the VPW path.
            bool destructive = this.writeType != WriteType.Compare && this.writeType != WriteType.TestWrite;
            if (destructive && !pcmInfo.IsSupportedWrite)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for write operations.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Abort");
                return false;
            }

            if (destructive && pcmInfo.IsUnderDevelopment)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.\r\nThere is additional brick risk in this operation.\r\nDo you want to continue?";
                logger.AddUserMessage(msg);
                if (await this.promptForYesNo(msg, "Brick Risk"))
                {
                    logger.AddUserMessage("User chose to proceed.");
                }
                else
                {
                    logger.AddUserMessage("User chose not to proceed.");
                    return false;
                }
            }

            CanCommands commands = this.vehicle.CreateCanCommands();

            logger.StatusUpdateActivity("Unlocking PCM...");
            if (!await commands.Unlock(pcmInfo, this.cancellationToken))
            {
                // On a user-requested abort the unlock simply stops; don't report it as a failure.
                if (!this.cancellationToken.IsCancellationRequested)
                {
                    logger.AddUserMessage("Unlock was not successful.");
                }
                return false;
            }
            logger.AddUserMessage("Unlock succeeded.");

            if (this.cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            DateTime start = DateTime.Now;
            CanKernelWriter writer = new CanKernelWriter(this.vehicle, commands, pcmInfo, this.writeType, this.logger);
            bool success = await writer.Write(image, validator, this.cancellationToken);
            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            return success;
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Accepts a byte array directly for OSes that don't support direct file handling.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the write was successful, fales if failed or aborted.</returns>
        public async Task<bool> Write(byte[] image, PcmType forcedPcmType = PcmType.Undefined)
        {
            // WriteType.None is not an operation; it means a caller never chose one (it is the CLR
            // default of the enum, so an unset UI binding produces it). Reject it here, before any
            // bus work: the writers only discover it at their block-selection switch, by which point
            // the kernel has already been uploaded and is running on the PCM, and the user sees an
            // "Unsuppported operation type" exception instead of a plain message.
            if (this.writeType == WriteType.None)
            {
                string msg = "Abort: no write type was selected.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Abort");
                return false;
            }

            // Sanity checks.
            PcmType? forcedFileType = forcedPcmType != PcmType.Undefined ? forcedPcmType : (PcmType?)null;
            FileValidator validator = new FileValidator(image, this.logger, forcedFileType);
            if (!validator.IdentifyAndValidate())
            {
                logger.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                return false;
            }
            logger.AddUserMessage("File OSID: " + validator.GetOsidFromImage());
            logger.AddUserMessage("File Description: " + new OSIDInfo(validator.GetFileType()).Description + ".");

            // Detect what is on the bus first (the shared first step, as in ReadManager). A CAN PCM is
            // written by a separate path that mirrors this one; the only user-visible difference is the
            // programming-mode setup. When a PCM type is forced we keep the VPW flow below.
            if (forcedPcmType == PcmType.Undefined)
            {
                DetectedModule? detected = await this.vehicle.DetectAndSelectPcm(this.cancellationToken);
                if (detected != null && detected.Bus == BusProtocol.Can500k)
                {
                    // The CAN path builds its profile from the file, not the connected PCM. We can only
                    // verify file-vs-hardware compatibility when the connected PCM's OSID resolves to a
                    // known type. When it does and it disagrees with the file, reject the mismatch - the
                    // same gate the VPW path applies below. When it does not resolve we cannot verify the
                    // hardware, so warn about the brick risk and let the user decide.
                    PcmType connectedType = new OSIDInfo(detected.Osid).HardwareType;
                    if (connectedType == PcmType.Undefined)
                    {
                        string msg = "PCM Hardware is not known/verified. The PCM may brick if the file is not compatible. Continue?";
                        logger.AddUserMessage(msg);
                        if (await this.promptForYesNo(msg, "Brick Risk"))
                        {
                            logger.AddUserMessage("User chose to proceed.");
                        }
                        else
                        {
                            logger.AddUserMessage("User chose to abort.");
                            return false;
                        }
                    }
                    else if (!validator.IsSameHardware(detected.Osid))
                    {
                        return false;
                    }
                    return await this.RunCanWrite(image, validator);
                }
                if (detected == null)
                {
                    // The quick probe found nothing; the device may have been left on CAN, so return it
                    // to VPW for the full VPW detection below (which has its own retries).
                    await this.vehicle.SelectBus(BusProtocol.Vpw);
                }
            }

            UInt64 kernelVersion = 0;
            bool needUnlock;
            int keyAlgorithm = 1;
            bool shouldHalt;
            OSIDInfo? pcmInfo = null;
            uint? pcmOsid = null;
            bool needToCheckOperatingSystem =
                (writeType != WriteType.OsPlusCalibrationPlusBoot) &&
                (writeType != WriteType.Full) &&
                (writeType != WriteType.TestWrite);

            if (forcedPcmType != PcmType.Undefined)
            {
                // A forced PCM type overrides which kernel and key algorithm we use (for example when
                // the OSID-to-type database is wrong for this PCM). It must NOT switch off the
                // file-vs-type compatibility check: otherwise an end user could force a type and flash
                // a file for completely different hardware, bricking it.
                pcmInfo = new OSIDInfo(forcedPcmType);
                keyAlgorithm = pcmInfo.KeyAlgorithm;
                needUnlock = true;
                needToCheckOperatingSystem = false;
                logger.AddUserMessage("Writing " + pcmInfo.HardwareType + " PCM.");

                // Confirm the file is for the PCM type the user selected. The manual selection IS the
                // declared PCM type, so we verify the file's own content matches it - we do NOT query
                // the PCM for an OSID here.
                PcmType fileType = validator.DetectFileType();
                if (fileType != forcedPcmType && !RuntimeSettings.AllowCrossFlashing)
                {
                    string msg = $"Abort: this file is for a {fileType} PCM, but {forcedPcmType} was selected.";
                    logger.AddUserMessage(msg);
                    await this.alert(msg, "Abort");
                    return false;
                }
                else if (this.cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (fileType != forcedPcmType && RuntimeSettings.AllowCrossFlashing)
                {
                    // Cross flashing override: the user has accepted the brick risk. Advanced
                    // recovery / developer use only (e.g. recovering a P05c BDM-flashed with a P05b bin).
                    logger.AddUserMessage($"Cross flashing enabled: writing a {fileType} file as {forcedPcmType}.");
                }

                // A forced CAN PCM (e.g. E38) is written by the CAN path, not the VPW flow below.
                // The dispatch is not here: it is below, after every branch has resolved pcmInfo, so
                // one place covers the forced type, a queried OSID, and a type inferred from the file.
            }
            else
            {
                logger.AddUserMessage("Requesting operating system ID...");
                Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                if (osidResponse.Status == ResponseStatus.Success)
                {
                    pcmInfo = new OSIDInfo(osidResponse.Value);
                    pcmOsid = osidResponse.Value;
                    keyAlgorithm = pcmInfo.KeyAlgorithm;
                    needUnlock = true;

                    if (!validator.IsSameHardware(osidResponse.Value))
                    {
                        return false;
                    }

                    if (!validator.IsSameOperatingSystem(osidResponse.Value))
                    {
                        logger.AddUserMessage("PCM operating system ID: " + osidResponse.Value);
                        logger.AddUserMessage("File operating system ID: " + validator.GetOsidFromImage());
                        Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, writeType, this.logger, out shouldHalt);
                        if (shouldHalt)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        logger.AddUserMessage("PCM and file are both operating system " + osidResponse.Value);
                    }

                    needToCheckOperatingSystem = false;
                }
                else
                {
                    if (this.cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    logger.AddUserMessage("Operating system request failed, checking for a live kernel...");

                    kernelVersion = await this.vehicle.GetKernelVersion(this.cancellationToken);
                    if (kernelVersion == 0)
                    {
                        // The PCM answered neither the OSID query nor the kernel version query, so it is
                        // almost certainly sitting in its boot loader (recovery). We cannot learn the type
                        // from the PCM, so the file's OSID supplies it and we try the unlock.
                        //
                        // There is deliberately no recovery probe here. A PCM in recovery announces itself
                        // by broadcasting unsolicited (0xA2, "programming prompt") - see
                        // Vehicle.CheckForRecoveryMode, which listens for exactly that. The old active
                        // "recovery query" sent mode 0x62 and parsed the reply, which is a programming-mode
                        // style request rather than recovery detection; it only ever worked on ObdLink
                        // ScanTool hardware and both of its outcomes did the same thing, so it decided
                        // nothing and has been removed. Use Tools -> PCM Recovery for an explicit recovery
                        // operation, where the user names the PCM type.
                        logger.AddUserMessage("PCM is not responding to OSID or kernel version checks; assuming recovery mode.");
                        logger.AddUserMessage("Unlock may not work, but we'll try...");
                        needUnlock = true;
                        pcmInfo = new OSIDInfo(validator.GetOsidFromImage()); // Prevent Null Reference Exceptions from breaking Recovery Mode
                    }
                    else
                    {
                        needUnlock = false;

                        logger.AddUserMessage("Kernel version: " + Vehicle.FormatKernelVersion(kernelVersion));

                        logger.AddUserMessage("Asking kernel for the PCM's operating system ID...");

                        if (needToCheckOperatingSystem)
                        {
                            osidResponse = await this.vehicle.QueryOperatingSystemIdFromKernel(this.cancellationToken);
                            if (osidResponse.Status != ResponseStatus.Success)
                            {
                                // The kernel seems broken. This shouldn't happen, but if it does, halt.
                                logger.AddUserMessage("The kernel did not respond to operating system ID query.");
                                return false;
                            }

                            Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, writeType, this.logger, out shouldHalt);
                            if (shouldHalt)
                            {
                                return false;
                            }

                            pcmInfo = new OSIDInfo(osidResponse.Value);
                        }

                        needToCheckOperatingSystem = false;
                    }
                }
            }

            // Select the bus once the PCM type is known, whichever branch above resolved it - the
            // forced type, an OSID queried from the PCM, or (when the PCM answers nothing and we
            // assume recovery) the type inferred from the file. That last route is why this sits
            // here rather than inside a branch: an E38 file against a silent bus resolved to a CAN
            // profile and then ran the whole VPW unlock/kernel flow anyway. ReadManager does the
            // same with the same helper.
            switch (await this.vehicle.PrepareBusFor(pcmInfo!))
            {
                case BusPreparation.Unavailable:
                    string busMsg = $"Abort: this device cannot use the CAN bus required by the {pcmInfo!.HardwareType} PCM.";
                    logger.AddUserMessage(busMsg);
                    await this.alert(busMsg, "Abort");
                    return false;

                case BusPreparation.Ready:
                    return await this.RunCanWrite(image, validator);
            }

            // Pre flight checks to block invalid write operations by PCM type.
            if (!pcmInfo!.IsSupported)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Abort");
                return false;
            }

            if (!pcmInfo.IsSupportedWrite)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for write operations.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Abort");
                return false;
            }

            if (pcmInfo.IsUnderDevelopment)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.\r\nThere is additional brick risk in this operation\r\nDo you want to continue?";
                logger.AddUserMessage(msg);
                if (await this.promptForYesNo(msg, "Brick Risk"))
                {
                    logger.AddUserMessage("User chose to proceed.");
                }
                else
                {
                    logger.AddUserMessage("User chose not to proceed.");
                    return false;
                }
            }

            // If the factory binary is not paritioned we cant write by segment, block the non-full write types
            if (!pcmInfo.IsSupportedWriteBySegment && (writeType == WriteType.Calibration || writeType == WriteType.OsPlusCalibrationPlusBoot || writeType == WriteType.Parameters))
            {
                string msg = $"Error: The connected {pcmInfo.HardwareType.ToString()} PCM binary format is not partitioned and does not support partial write." + Environment.NewLine +
                            "You will need to do a Write Full Flash (Clone) instead.";
                logger.AddUserMessage(msg);
                await this.alert(msg, "Error");
                return false;
            }

            // If we cant write the slave, warn the user of operating system changes, if there are any.
            // Skip the warning if we know the file OS matches the PCM OS - no slave CPU sync needed.
            bool osWillChange = pcmOsid == null || !validator.IsSameOperatingSystem(pcmOsid.Value);
            if (pcmInfo.HardwareSlaveCPU == true && !pcmInfo.IsSupportedWriteSlaveCPU && (writeType == WriteType.Full || writeType == WriteType.OsPlusCalibrationPlusBoot) && osWillChange)
            {
                string msg = $"Warning: Writes to the {pcmInfo.HardwareType.ToString()} slave CPU are not supported." + Environment.NewLine +
                            "When you change the operating system you need need another way to update the slave CPU to match, else electroncic throttle may not work." + Environment.NewLine +
                            "PCM Hammer can re-write the original OS to undo any change if kept backup." + Environment.NewLine +
                            "Do you want to continue?";
                logger.AddUserMessage(msg);
                if (await this.promptForYesNo(msg, "Warning!"))
                {
                    logger.AddUserMessage("User chose to proceed.");
                }
                else
                { 
                    logger.AddUserMessage("User chose not to proceed.");
                    return false;
                }
            }

            /*if (pcmInfo.HardwareType == PcmType.E54)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} support is insufficiently tested, but believed to be working." + Environment.NewLine +
                            "Please report success or failure on pcmhacking.net." + Environment.NewLine +
                            "Do you accept the risk of damage to your hardware?";
                this.AddUserMessage(msg);
                DialogResult dialogResult = MessageBox.Show(msg, "Continue?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    this.AddUserMessage("User chose not to proceed.");
                    return;
                }else
                {
                    this.AddUserMessage("User accepts the risk of running insufficiently tested code.");
                }
            }*/

            await this.vehicle.SuppressChatter();

            if (needUnlock)
            {

                bool unlocked = await this.vehicle.UnlockEcu(keyAlgorithm, this.cancellationToken);
                if (!unlocked)
                {
                    logger.AddUserMessage("Unlock was not successful.");
                    return false;
                }

                logger.AddUserMessage("Unlock succeeded.");
            }

            DateTime start = DateTime.Now;

            CKernelWriter writer = new CKernelWriter(
                this.vehicle,
                pcmInfo,
                new Protocol(),
                writeType,
                this.logger);

            await writer.Write(
                image,
                kernelVersion,
                validator,
                needToCheckOperatingSystem,
                this.cancellationToken);
            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            return true;
        }
    }
}

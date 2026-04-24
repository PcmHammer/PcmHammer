using PcmHacking.ECU;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Contains flash-writing code shared by the WinForms and Uno user interfaces.
    /// </summary>
    public class WriteManager : IControllerManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private ECUActionArguments _actionArguments;
        private ControllerPageObjects _pageObjects;
        private CancellationToken cancellationToken;
        private IProgress<ProgressUpdate>? progress;

        public WriteManager(
            ILogger logger,
            Vehicle vehicle,
            ECUActionArguments actionArguments,
            ControllerPageObjects controllerPageObjects,
            CancellationToken cancellationToken,
            IProgress<ProgressUpdate> progress)
        {
            this.logger = logger;
            this.vehicle = vehicle;
            _actionArguments = actionArguments;
            _pageObjects = controllerPageObjects;
            this.cancellationToken = cancellationToken;
            this.progress = progress;
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Overloaded method that the utilizes Write(byte[]).
        /// Accepts a string path for OSes that can directly access file structure. Kept here for compatability reasons.
        /// </summary>
        /// <returns>True if file opens and write succeeds. False if either condition fails.</returns>
        public async Task<bool> Begin(string path)
        {
            byte[] image;
            using (Stream stream = File.OpenRead(path))
            {
                image = new byte[stream.Length];
                int bytesRead = await stream.ReadAsync(image, 0, (int)stream.Length);
                if (bytesRead != stream.Length)
                {
                    // If this happens too much, we should try looping rather than reading the whole file in one shot.
                    this.logger.AddUserMessage("Unable to load file.");
                    return false;
                }
            }
            return await Begin(new MemoryStream(image));
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Accepts a byte array directly for OSes that don't support direct file handling.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the write was successful, fales if failed or aborted.</returns>
        public async Task<bool> Begin(MemoryStream? contentStream)
        {
            byte[] image = contentStream?.ToArray() ?? [];
            // Sanity checks. 
            FileValidator validator = new FileValidator(image, this.logger);
            if (!validator.IsValid())
            {
                this.logger.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                return false;
            }

            UInt32 kernelVersion = 0;
            bool needUnlock = true;
            int keyAlgorithm = 1;
            bool shouldHalt;
            ECUBase pcmInfo = null;
            bool skipChecks = false;

            bool needToCheckOperatingSystem =
                (_actionArguments.WriteType != WriteType.OsPlusCalibrationPlusBoot) &&
                (_actionArguments.WriteType != WriteType.Full) &&
                (_actionArguments.WriteType != WriteType.Test);

            switch (vehicle.ECUState)
            {
                case ECUStates.Programmed:
                    pcmInfo = vehicle.ConnectedECU;
                    keyAlgorithm = pcmInfo.KeyAlgorithm;
                    skipChecks = true;
                    break;
                case ECUStates.Kernel:
                    this.logger.AddUserMessage("PCM is in kernel mode.");
                    needUnlock = false;
                    if (needToCheckOperatingSystem)
                    {
                        Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemIdFromKernel(this.cancellationToken);
                        if (osidResponse.Status != ResponseStatus.Success)
                        {
                            // The kernel seems broken. This shouldn't happen, but if it does, halt.
                            this.logger.AddUserMessage("The kernel did not respond to operating system ID query.");
                            return false;
                        }

                        Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, _actionArguments.WriteType, this.logger, out shouldHalt);
                        if (shouldHalt)
                        {
                            return false;
                        }

                        pcmInfo = ECUFactory.GetControllerByOSID(osidResponse.Value);
                    }

                    needToCheckOperatingSystem = false;
                    skipChecks = true;
                    break;
                case ECUStates.Recovery:
                    this.logger.AddUserMessage("PCM is in recovery mode.");
                    pcmInfo = ECUFactory.GetControllerByOSID(validator.GetOsidFromImage()); // Prevent Null Reference Exceptions from breaking Recovery Mode
                    skipChecks = true;
                    break;
            }


            if (!skipChecks)
            {
                this.logger.AddUserMessage("Requesting operating system ID...");
                Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(this.cancellationToken);
                if (osidResponse.Status == ResponseStatus.Success)
                {
                    pcmInfo = ECUFactory.GetControllerByOSID(osidResponse.Value);
                    vehicle.ConnectedECU = pcmInfo;
                    keyAlgorithm = pcmInfo.KeyAlgorithm;
                    needUnlock = true;

                    if (!validator.IsSameHardware(osidResponse.Value))
                    {
                        return false;
                    }

                    if (!validator.IsSameOperatingSystem(osidResponse.Value))
                    {
                        Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, _actionArguments.WriteType, this.logger, out shouldHalt);
                        if (shouldHalt)
                        {
                            return false;
                        }
                    }

                    needToCheckOperatingSystem = false;
                }
                else
                {
                    if (this.cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    this.logger.AddUserMessage("Operating system request failed, checking for a live kernel...");

                    kernelVersion = await this.vehicle.GetKernelVersion();
                    if (kernelVersion == 0)
                    {
                        this.logger.AddUserMessage("Checking for recovery mode...");
                        bool recoveryMode = await this.vehicle.IsInRecoveryMode();

                        if (recoveryMode)
                        {
                            this.logger.AddUserMessage("PCM is in recovery mode.");
                            needUnlock = true;
                        }
                        else
                        {
                            this.logger.AddUserMessage("PCM is not responding to OSID, kernel version, or recovery mode checks.");
                            this.logger.AddUserMessage("Unlock may not work, but we'll try...");
                            needUnlock = true;
                        }
                        pcmInfo = ECUFactory.GetControllerByOSID(validator.GetOsidFromImage()); // Prevent Null Reference Exceptions from breaking Recovery Mode
                    }
                    else
                    {
                        needUnlock = false;

                        this.logger.AddUserMessage("Kernel version: " + kernelVersion.ToString("X8"));

                        this.logger.AddUserMessage("Asking kernel for the PCM's operating system ID...");

                        if (needToCheckOperatingSystem)
                        {
                            osidResponse = await this.vehicle.QueryOperatingSystemIdFromKernel(this.cancellationToken);
                            if (osidResponse.Status != ResponseStatus.Success)
                            {
                                // The kernel seems broken. This shouldn't happen, but if it does, halt.
                                this.logger.AddUserMessage("The kernel did not respond to operating system ID query.");
                                return false;
                            }

                            Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, _actionArguments.WriteType, this.logger, out shouldHalt);
                            if (shouldHalt)
                            {
                                return false;
                            }

                            pcmInfo = ECUFactory.GetControllerByOSID(osidResponse.Value);
                        }

                        needToCheckOperatingSystem = false;
                    }
                }
            }
            if (pcmInfo == null)
            {
                throw new NullReferenceException(nameof(pcmInfo));
            }
            if (!pcmInfo.IsSupported && _actionArguments.HardwareType != PcmType.Undefined)
            {
                this.logger.AddUserMessage("Detected hardware type override on undefined ECU. Please be sure to post results!");
                pcmInfo = ECUFactory.GetControllerOverride(_actionArguments.HardwareType, pcmInfo.GetCurrentOSID());
                pcmInfo.HardwareTypeOverridden = true;
                this.logger.AddUserMessage($"Continuing read with hardware type of {_actionArguments.HardwareType}");
            }

            if (_actionArguments.PreFlightChecksRequired)
            {
                // Pre flight checks to block invalid write operations by PCM type.
                if (!pcmInfo.IsSupported)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                    this.logger.AddUserMessage(msg);
                    await _pageObjects.ShowAlert(msg, "Abort");
                    return false;
                }

                if (!pcmInfo.IsSupportedWrite)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for write operations.";
                    this.logger.AddUserMessage(msg);
                    await _pageObjects.ShowAlert(msg, "Abort");
                    return false;
                }

                if (pcmInfo.IsUnderDevelopment)
                {
                    string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.\r\nThere is additional brick risk in this operation\r\nDo you want to continue?";
                    this.logger.AddUserMessage(msg);
                    if (await _pageObjects.PromptYesOrNo(msg, "Brick Risk"))
                    {
                        this.logger.AddUserMessage("User chose to proceed.");
                    }
                    else
                    {
                        this.logger.AddUserMessage("User chose not to proceed.");
                        return false;
                    }
                }
            }

            // If the factory binary is not paritioned we cant write by segment, block the non-full write types
            if (!pcmInfo.IsSupportedWriteBySegment && (_actionArguments.WriteType == WriteType.Calibration || _actionArguments.WriteType == WriteType.OsPlusCalibrationPlusBoot || _actionArguments.WriteType == WriteType.Parameters))
            {
                string msg = $"Error: The connected {pcmInfo.HardwareType.ToString()} PCM binary format is not partitioned and does not support partial write." + Environment.NewLine +
                            "You will need to do a Write Full Flash (Clone) instead.";
                this.logger.AddUserMessage(msg);
                await _pageObjects.ShowAlert(msg, "Error");
                return false;
            }

            // If we cant write the slave, warn the user of operating system changes
            if (pcmInfo.HardwareSlaveCPU == true && !pcmInfo.IsSupportedWriteSlaveCPU && (_actionArguments.WriteType == WriteType.Full || _actionArguments.WriteType == WriteType.OsPlusCalibrationPlusBoot))
            {
                string msg = $"Warning: Writes to the {pcmInfo.HardwareType.ToString()} slave CPU are not supported." + Environment.NewLine +
                            "You must have another way to update the slave CPU to match when you change operating system, else electroncic throttle may not work." + Environment.NewLine +
                            "Restore this PCM to its original operating system if this happens." + Environment.NewLine +
                            "Do you want to continue?";
                this.logger.AddUserMessage(msg);
                if (await _pageObjects.PromptYesOrNo(msg, "Warning!"))
                {
                    this.logger.AddUserMessage("User chose to proceed.");
                }
                else
                { 
                    this.logger.AddUserMessage("User chose not to proceed.");
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

                bool unlocked = await this.vehicle.UnlockEcu(keyAlgorithm);
                if (!unlocked)
                {
                    this.logger.AddUserMessage("Unlock was not successful.");
                    return false;
                }

                this.logger.AddUserMessage("Unlock succeeded.");
            }

            DateTime start = DateTime.Now;

           CKernelWriter writer = new CKernelWriter(
                this.vehicle,
                pcmInfo,
                new Protocol(),
                _actionArguments.WriteType,
                this.logger,
                progress);

            await writer.Write(
                image,
                kernelVersion,
                validator,
                needToCheckOperatingSystem,
                this.cancellationToken);
            this.logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            return true;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
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
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Overloaded method that the utilizes Write(byte[]).
        /// Accepts a string path for OSes that can directly access file structure.
        /// </summary>
        /// <returns>True if file opens and write succeeds. False if either condition fails.</returns>
        public async Task<bool> Write(string path, PcmType forcedPcmType = PcmType.Undefined)
        {
            byte[] image;
            using (Stream stream = File.OpenRead(path))
            {
                image = new byte[stream.Length];
                int bytesRead = await stream.ReadAsync(image, 0, (int)stream.Length);
                if (bytesRead != stream.Length)
                {
                    // If this happens too much, we should try looping rather than reading the whole file in one shot.
                    logger.AddUserMessage("Unable to load file.");
                    return false;
                }
            }
            return await Write(image, forcedPcmType);
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
            // Sanity checks.
            PcmType? forcedFileType = forcedPcmType != PcmType.Undefined ? forcedPcmType : (PcmType?)null;
            FileValidator validator = new FileValidator(image, this.logger, forcedFileType);
            if (!validator.IsValid())
            {
                logger.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                return false;
            }
            logger.AddUserMessage("File is " + new OSIDInfo(validator.GetFileType()).Description + ".");

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
                pcmInfo = new OSIDInfo(forcedPcmType);
                keyAlgorithm = pcmInfo.KeyAlgorithm;
                needUnlock = true;
                needToCheckOperatingSystem = false;
                logger.AddUserMessage("Using manually selected PCM type: " + pcmInfo.HardwareType);
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

                    kernelVersion = await this.vehicle.GetKernelVersion();
                    if (kernelVersion == 0)
                    {
                        logger.AddUserMessage("Checking for recovery mode...");
                        bool recoveryMode = await this.vehicle.IsInRecoveryMode();

                        if (recoveryMode)
                        {
                            logger.AddUserMessage("PCM is in recovery mode.");
                            needUnlock = true;
                        }
                        else
                        {
                            logger.AddUserMessage("PCM is not responding to OSID, kernel version, or recovery mode checks.");
                            logger.AddUserMessage("Unlock may not work, but we'll try...");
                            needUnlock = true;
                        }
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
            // Skip the warning if we know the file OS matches the PCM OS — no slave CPU sync needed.
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

                bool unlocked = await this.vehicle.UnlockEcu(keyAlgorithm);
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

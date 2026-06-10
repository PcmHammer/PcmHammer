// SPDX-License-Identifier: GPL-3.0-only
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

        // NOTE FROM CROWBAR: This function violates the goal of separating Platform calls and UI from backend. My goal is to retire this entirely.
        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Overloaded method that the utilizes Write(byte[]).
        /// Accepts a string path for OSes that can directly access file structure. Kept here for compatibility reasons.
        /// </summary>
        /// <returns>True if file opens and write succeeds. False if either condition fails.</returns>
        public async Task<Response<bool>> Begin(string path)
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
                    return Response.Create(ResponseStatus.Error, false, 0);
                }
            }
            _actionArguments.ContentStream = new MemoryStream(image);
            return await Begin();
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// Accepts a byte array directly for OSes that don't support direct file handling.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the write was successful, fales if failed or aborted.</returns>
        public async Task<Response<bool>> Begin()
        {
            // We should already be aware of the connected controller by now.
            if (this.vehicle.ConnectedECU == null)
            {
                throw new NullReferenceException("vehicle.ConnectedECU was null!");
            }
            // We should have arguments set before getting here.
            if (_actionArguments == null)
            {
                throw new NullReferenceException($"{nameof(_actionArguments)} was null.");
            }
            byte[] image = _actionArguments.ContentStream?.ToArray() ?? [];
            // Sanity checks. 
            FileValidator validator = new FileValidator(image, this.logger);
            if (!validator.IsValid())
            {
                this.logger.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                return Response.Create(ResponseStatus.Error, false, 0);
            }
            this.logger.AddUserMessage("File is " + ECUFactory.GetControllerOverride(validator.GetFileType()).ToString() + ".");

            ECUBase? pcmInfo = vehicle.ConnectedECU;
            UInt32 kernelVersion = 0;
            bool needUnlock = true;
            int keyAlgorithm = pcmInfo?.KeyAlgorithm ?? 1;
            bool shouldHalt;

            bool needToCheckOperatingSystem =
                (_actionArguments.WriteType != WriteType.OsPlusCalibrationPlusBoot) &&
                (_actionArguments.WriteType != WriteType.Full) &&
                (_actionArguments.WriteType != WriteType.Test);

            // Stripped down to simple, per-state validation.
            switch (vehicle.ConnectedECU.ECUState)
            {
                case ECUStates.Invalid:
                    pcmInfo = ECUFactory.GetControllerByOSID(validator.GetOsidFromImage());
                    break;

                case ECUStates.Programmed:
                    if (pcmInfo == null)
                        return Response.Create(ResponseStatus.Error, false, 0);
                    if (!validator.IsSameHardware(pcmInfo.GetCurrentOSID()))
                    {
                        return Response.Create(ResponseStatus.Error, false, 0);
                    }

                    if (!validator.IsSameOperatingSystem(pcmInfo.GetCurrentOSID()))
                    {
                        logger.AddUserMessage("PCM operating system ID: " + pcmInfo.GetCurrentOSID());
                        logger.AddUserMessage("File operating system ID: " + validator.GetOsidFromImage());
                        Utility.ReportOperatingSystems(validator.GetOsidFromImage(), pcmInfo.GetCurrentOSID(), _actionArguments.WriteType, this.logger, out shouldHalt);
                        if (shouldHalt)
                        {
                            return Response.Create(ResponseStatus.Error, false, 0);
                        }
                    }
                    else
                    {
                        logger.AddUserMessage("PCM and file are both operating system " + pcmInfo.GetCurrentOSID());
                    }
                    needToCheckOperatingSystem = false;
                    break;

                case ECUStates.Recovery:
                    pcmInfo = ECUFactory.GetControllerByOSID(validator.GetOsidFromImage());
                    needUnlock = false;
                    break;

                case ECUStates.Kernel:
                    if (pcmInfo == null)
                        return Response.Create(ResponseStatus.Error, false, 0);
                    needUnlock = false;
                    Utility.ReportOperatingSystems(validator.GetOsidFromImage(), pcmInfo.GetCurrentOSID(), _actionArguments.WriteType, this.logger, out shouldHalt);
                    if (shouldHalt)
                    {
                        return Response.Create(ResponseStatus.Error, false, 0);
                    }
                    needToCheckOperatingSystem = false;
                    break;
            }

            if (pcmInfo == null)
            {
                throw new NullReferenceException(nameof(pcmInfo));
            }
            if (!pcmInfo.IsSupported && _actionArguments.HardwareType != PcmType.Undefined)
            {
                this.logger.AddUserMessage("Detected hardware type override on undefined ECU. Please be sure to post results!");
                pcmInfo = ECUFactory.GetControllerOverride(_actionArguments.HardwareType);
                pcmInfo.HardwareTypeOverridden = true;
                this.logger.AddUserMessage($"Continuing read with hardware type of {_actionArguments.HardwareType}");
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
                    return Response.Create(ResponseStatus.Error, false, 0);
                }

                logger.AddUserMessage("Unlock succeeded.");
            }

            DateTime start = DateTime.Now;

            vehicle.Enable4xReadWrite = _actionArguments.UseHighSpeed || vehicle.Enable4xReadWrite;

            CKernelWriter writer = new CKernelWriter(
                this.vehicle,
                pcmInfo,
                new Protocol(),
                _actionArguments,
                this.logger,
                progress);

            await writer.Write(
                image,
                kernelVersion,
                validator,
                needToCheckOperatingSystem,
                this.cancellationToken);
            this.logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            return Response.Create(ResponseStatus.Success, true, 0);
        }
    }
}

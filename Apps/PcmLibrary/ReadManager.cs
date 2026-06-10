// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.ECU;
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
    public class ReadManager : IControllerManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private ControllerPageObjects _pageObjects;
        private ECUActionArguments _actionArguments;
        private CancellationToken cancellationToken;
        private IProgress<ProgressUpdate>? _progress;

        public int CrcPollingDelayMs { get; set; } = 50;

        public ReadManager(
            ILogger logger,
            Vehicle vehicle,
            ECUActionArguments actionArguments,
            ControllerPageObjects pageObjects,
            CancellationToken cancellationToken,
            IProgress<ProgressUpdate>? progress
            )
        {
            this.logger = logger;
            this.vehicle = vehicle;
            _actionArguments = actionArguments;
            _pageObjects = pageObjects;
            this.cancellationToken = cancellationToken;
            _progress = progress;
        }

        // NOTE FROM CROWBAR: This function violates the goal of separating UI from backend. My goal is to retire this entirely.
        public async Task<Response<bool>> Begin(string path)
        {
            Response<bool> readResponse = await Begin();
            if (readResponse == null || readResponse.Value)
            {
                return readResponse ?? Response.Create(ResponseStatus.Error, false, 0);
            }

            MemoryStream? readContents = _actionArguments.ContentStream;

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
                    this.logger.AddUserMessage("Saving contents to " + path);

                    readContents.Position = 0;

                    using (Stream output = File.Open(path, FileMode.Create))
                    {
                        await readContents.CopyToAsync(output);
                    }
                    return Response.Create(ResponseStatus.Success, false, 0);
                }
                catch (IOException exception)
                {
                    logger.AddUserMessage("Unable to save file: " + exception.Message);
                    logger.AddDebugMessage(exception.ToString());

                    await _pageObjects.Invoke(async () => path = await _pageObjects.PromptForSavePath());
                    if (path == null)
                    {
                        logger.AddUserMessage("Save canceled.");

                        // Returning true to indicate that the read worked. It doesn't
                        // really matter that the user chose not to keep the file.
                        return Response.Create(ResponseStatus.Cancelled, false, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to read the PCM's flash memory.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the read was successful, fales if failed or aborted.</returns>
        public async Task<Response<bool>> Begin()
        {
            if(vehicle.ConnectedECU == null)
            {
                throw new NullReferenceException("vehicle.ConnectedECU was null!");
            }
            if(_actionArguments == null)
            {
                throw new NullReferenceException($"{nameof(_actionArguments)} was null.");
            }
            ECUBase? pcmInfo = vehicle.ConnectedECU;
                switch (vehicle.ConnectedECU.ECUState)
                {
                    case ECUStates.Invalid: 
                    break;
                    case ECUStates.Programmed:
                        logger.AddUserMessage("OSID: " + pcmInfo.GetCurrentOSID());
                        logger.AddUserMessage("Description: " + pcmInfo.ToString());
                        break;
                    case ECUStates.Kernel: // What should we be doing for a PCM already in kernel mode?
                        break;
                    case ECUStates.Recovery: // Refuse to read? Unsure of the possible intent here.
                        break;
                }
            if (pcmInfo == null)
            {
                throw new NullReferenceException(nameof(pcmInfo));
            }
            if (!pcmInfo.IsSupported && _actionArguments.HardwareType != PcmType.Undefined)
            {
                logger.AddUserMessage("Detected hardware type override on undefined ECU. Please be sure to post results!");
                pcmInfo = ECUFactory.GetControllerOverride(_actionArguments.HardwareType);
                logger.AddUserMessage($"Continuing read with hardware type of {_actionArguments.HardwareType}");
            }

                // These tests are retired here, left only for reference at the moment. We can now call ECUBase.GetPreCheckResults() to determine whether to display a prompt.
            if (_actionArguments.PreFlightChecksRequired)
            {
                // Pre flight checks to block invalid write operations by PCM type.
                if (!pcmInfo.IsSupported)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                    logger.AddUserMessage(msg);
                    await _pageObjects.Invoke(async () => await _pageObjects.ShowAlert(msg, "Abort"));
                    return Response.Create(ResponseStatus.Refused, false, 0);
                }

                if (!pcmInfo.IsSupportedRead)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                    logger.AddUserMessage(msg);
                    await _pageObjects.Invoke(async () => await _pageObjects.ShowAlert(msg, "Abort"));
                    return Response.Create(ResponseStatus.Refused, false, 0);
                }

                if (pcmInfo.IsUnderDevelopment)
                {
                    string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                    logger.AddUserMessage(msg);
                    bool shouldContinue = false;
                    await _pageObjects.Invoke(async () => { shouldContinue = await _pageObjects.PromptYesOrNo(msg, "Continue?"); });
                    if (!shouldContinue)
                    {
                        logger.AddUserMessage("User chose not to proceed.");
                        return Response.Create(ResponseStatus.Refused, false, 0);
                    }
                }
            }

            await vehicle.SuppressChatter();

            bool unlocked = await vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
            if (!unlocked)
            {
                logger.AddUserMessage("Unlock was not successful.");
                return Response.Create(ResponseStatus.Error, false, 0);
            }

            logger.AddUserMessage("Unlock succeeded.");

            if (cancellationToken.IsCancellationRequested)
            {
                return Response.Create(ResponseStatus.Cancelled, false, 0);
            }

            DateTime start = DateTime.Now;

            CKernelReader reader = new CKernelReader(
                vehicle,
                pcmInfo,
                this.logger,
                _progress ?? new Progress<ProgressUpdate>())
            {
                CrcPollingDelayMs = this.CrcPollingDelayMs,
            };

            vehicle.Enable4xReadWrite = _actionArguments.UseHighSpeed || vehicle.Enable4xReadWrite;

            Response<Stream?> readResponse = await reader.ReadContents(cancellationToken);

            logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

            if (readResponse.Status != ResponseStatus.Success && readResponse.Status != ResponseStatus.Unverified)
            {
                logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
            }
            return Response.Create(readResponse.Status, readResponse.Status == ResponseStatus.Success, readResponse.RetryCount); // Hybrid compromise: I see the usefulness of a returned status flag, returning the stream this way however makes things funky with WriteManager's Begin() method.
        }

        private static string GetBadReadPath(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            return Path.Combine(dir, name + "_badread" + ext);
        }
    }
}

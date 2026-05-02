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
        private ILogger _logger;
        private Vehicle _vehicle;
        private ControllerPageObjects _pageObjects;
        private ECUActionArguments _actionArguments;
        private CancellationToken _cancellationToken;
        private IProgress<ProgressUpdate>? _progress;

        public ReadManager(
            ILogger logger,
            Vehicle vehicle,
            ECUActionArguments actionArguments,
            ControllerPageObjects pageObjects,
            CancellationToken cancellationToken,
            IProgress<ProgressUpdate>? progress
            )
        {
            _logger = logger;
            _vehicle = vehicle;
            _actionArguments = actionArguments;
            _pageObjects = pageObjects;
            _cancellationToken = cancellationToken;
            _progress = progress;
        }

        public async Task<bool> Begin(string path)
        {
            MemoryStream? readContents = new();
            await Begin(readContents);
            if (readContents == null)
            {
                return false;
            }
            // Save the contents to the path that the user provided.
            while (true)
            {
                try
                {
                    _logger.AddUserMessage("Saving contents to " + path);

                    readContents.Position = 0;

                    using (Stream output = File.Open(path, FileMode.Create))
                    {
                        await readContents.CopyToAsync(output);
                    }

                    return true;
                }
                catch (IOException exception)
                {
                    _logger.AddUserMessage("Unable to save file: " + exception.Message);
                    _logger.AddDebugMessage(exception.ToString());

                    await _pageObjects.Invoke(async () => path = await _pageObjects.PromptForSavePath());
                    if (path == null)
                    {
                        _logger.AddUserMessage("Save canceled.");

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
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the read was successful, fales if failed or aborted.</returns>
        public async Task<bool> Begin(MemoryStream? contentStream)
        {
            if(_actionArguments == null)
            {
                throw new NullReferenceException($"{nameof(_actionArguments)} was null.");
            }
            ECUBase? pcmInfo = null;
                switch (_vehicle.ECUState)
                {
                    case ECUStates.Invalid: // This really only exists for WinForms now, this all is handled by the ConnectionService.
                        _logger.AddUserMessage("Querying operating system of current PCM.");
                        Response<uint> osidResponse = await _vehicle.QueryOperatingSystemId(_cancellationToken);
                        if (osidResponse.Status != ResponseStatus.Success)
                        {
                            _logger.AddUserMessage("Operating system query failed, will retry: " + osidResponse.Status);
                            await _vehicle.ExitKernel();

                            osidResponse = await _vehicle.QueryOperatingSystemId(_cancellationToken);
                            if (osidResponse.Status != ResponseStatus.Success)
                            {
                                _logger.AddUserMessage("Operating system query failed: " + osidResponse.Status);
                            }
                        }
                        if (osidResponse.Status == ResponseStatus.Success)
                        {
                            // Look up the information about this PCM, based on the OSID;
                            _logger.AddUserMessage("OSID: " + osidResponse.Value);
                            pcmInfo = ECUFactory.GetControllerByOSID(osidResponse.Value);
                            _logger.AddUserMessage("Description: " + pcmInfo.ToString());
                        }
                        else
                        {
                            _logger.AddUserMessage("Unable to get operating system ID. Will assume this can be unlocked with the default seed/key algorithm.");

                            UInt32 OperatingSystemId = 0;

                            await _vehicle.ForceSendToolPresentNotification();
                            await _pageObjects.Invoke(async () => OperatingSystemId = await _pageObjects.PromptForHardwareType()); // One would say I should gaurd this here too (UI call), but in theory we should never reach this.
                            await _vehicle.ForceSendToolPresentNotification();

                            pcmInfo = ECUFactory.GetControllerByOSID(OperatingSystemId); // osid

                            _logger.AddUserMessage($"Using OsID: {pcmInfo.GetCurrentOSID()}");
                        }
                        break;
                    case ECUStates.Programmed:
                        pcmInfo = _vehicle.ConnectedECU;
                        _logger.AddUserMessage("OSID: " + pcmInfo.GetCurrentOSID());
                        _logger.AddUserMessage("Description: " + pcmInfo.ToString());
                        break;
                    case ECUStates.Kernel: // These will be handled down the line.
                        _logger.AddUserMessage("PCM is in kernel mode.");
                            osidResponse = await _vehicle.QueryOperatingSystemIdFromKernel(_cancellationToken);
                            if (osidResponse.Status != ResponseStatus.Success)
                            {
                                // The kernel seems broken. This shouldn't happen, but if it does, halt.
                                _logger.AddUserMessage("The kernel did not respond to operating system ID query.");
                                return false;
                            }
                            pcmInfo = ECUFactory.GetControllerByOSID(osidResponse.Value);
                        break;
                    case ECUStates.Recovery: // Handled by hardware overrride 
                        break;
                }
            if (pcmInfo == null)
            {
                throw new NullReferenceException(nameof(pcmInfo));
            }
            if(_vehicle.ConnectedECU == null)
            {
                _vehicle.ConnectedECU = pcmInfo;
            }

            if (!pcmInfo.IsSupported && _actionArguments.HardwareType != PcmType.Undefined)
            {
                _logger.AddUserMessage("Detected hardware type override on Unsupported ECU. Please be sure to post results!");
                pcmInfo = ECUFactory.GetControllerOverride(_actionArguments.HardwareType, pcmInfo.GetCurrentOSID());
                _logger.AddUserMessage($"Continuing read with hardware type of {_actionArguments.HardwareType}");
            }

                // These tests want the UI, but this library doesn't behave with UNO's. These tests can be now be found in ControllerActionSetup. Left here under a conditional only for temporary backwards compat with WinForms.
            if (_actionArguments.PreFlightChecksRequired)
            {
                // Pre flight checks to block invalid write operations by PCM type.
                if (!pcmInfo.IsSupported)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                    _logger.AddUserMessage(msg);
                    await _pageObjects.Invoke(async () => await _pageObjects.ShowAlert(msg, "Abort"));
                    return false;
                }

                if (!pcmInfo.IsSupportedRead)
                {
                    string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                    _logger.AddUserMessage(msg);
                    await _pageObjects.Invoke(async () => await _pageObjects.ShowAlert(msg, "Abort"));
                    return false;
                }

                if (pcmInfo.IsUnderDevelopment)
                {
                    string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                    _logger.AddUserMessage(msg);
                    bool shouldContinue = false;
                    await _pageObjects.Invoke(async () => { shouldContinue = await _pageObjects.PromptYesOrNo(msg, "Continue?"); });
                    if (!shouldContinue)
                    {
                        _logger.AddUserMessage("User chose not to proceed.");
                        return false;
                    }
                }
            }

            await _vehicle.SuppressChatter();

            bool unlocked = await _vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
            if (!unlocked)
            {
                _logger.AddUserMessage("Unlock was not successful.");
                return false;
            }

            _logger.AddUserMessage("Unlock succeeded.");

            if (_cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // Do the actual reading.
            DateTime start = DateTime.Now;

            CKernelReader reader = new CKernelReader(
                _vehicle,
                pcmInfo,
                _logger,
                _progress ?? new Progress<ProgressUpdate>());

            _vehicle.Enable4xReadWrite = _actionArguments.UseHighSpeed || _vehicle.Enable4xReadWrite;

            Response<Stream> readResponse = await reader.ReadContents(_cancellationToken);

            _logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            if (readResponse.Status != ResponseStatus.Success)
            {
                _logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                return false;
            }
            if (contentStream != null)
            {
                readResponse.Value.CopyTo(contentStream);
                contentStream.Position = 0;
            }
            return true;
        }
    }
}

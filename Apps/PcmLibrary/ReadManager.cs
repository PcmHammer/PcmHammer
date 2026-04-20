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

        public async Task<bool> Read(string path)
        {
            Stream? readContents = await Read();
            if (readContents == null)
            {
                return false;
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
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the read was successful, fales if failed or aborted.</returns>
        public async Task<Stream?> Read()
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

            OSIDInfo pcmInfo;
            if (osidResponse.Status == ResponseStatus.Success)
            {
                // Look up the information about this PCM, based on the OSID;
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

                pcmInfo = new OSIDInfo(OperatingSystemId); // osid

                this.logger.AddUserMessage($"Using OsID: {pcmInfo.OSID}");
            }

            // Pre flight checks to block invalid write operations by PCM type.
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

            if (pcmInfo.HardwareType == PcmType.P05)
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

            // Do the actual reading.
            DateTime start = DateTime.Now;

            CKernelReader reader = new CKernelReader(
                this.vehicle,
                pcmInfo,
                this.logger);

            Response<Stream> readResponse = await reader.ReadContents(this.cancellationToken);

            this.logger.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
            if (readResponse.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                return null;
            }
            return readResponse.Value;
        }
    }
}

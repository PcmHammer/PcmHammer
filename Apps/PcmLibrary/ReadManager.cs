using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Contains flash-reading code shared by the WinForms and Uno user interfaces.
    /// </summary>
    public class ReadManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private Func<Action, object> invoke;
        private Func<Task<string>> promptForFilePath;
        private Func<Task<UInt32>> promptForOperatingSystemId;
        private CancellationToken cancellationToken;

        public ReadManager(
            ILogger logger, 
            Vehicle vehicle, 
            Func<Action, object> invoke, 
            Func<Task<string>> promptForFilePath,
            Func<Task<UInt32>> promptForOperatingSystemId,
            CancellationToken cancellationToken)
        {
            this.logger = logger;
            this.vehicle = vehicle;
            this.invoke = invoke;
            this.promptForFilePath = promptForFilePath;
            this.promptForOperatingSystemId = promptForOperatingSystemId;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to read the PCM's flash memory.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the read was successful, fales if failed or aborted.</returns>
        public async Task<bool> Read()
        {
            // Get the path to save the image to.
            string path = "";
            this.invoke(async () => path = await this.promptForFilePath());

            if (path == null)
            {
                this.logger.AddUserMessage("Read canceled.");
                return false;
            }

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
                this.invoke(async () => OperatingSystemId = await this.promptForOperatingSystemId());
                await this.vehicle.ForceSendToolPresentNotification();

                pcmInfo = new OSIDInfo(OperatingSystemId); // osid

                this.logger.AddUserMessage($"Using OsID: {pcmInfo.OSID}");
            }

            // Pre flight checks to block invalid write operations by PCM type.
            if (!pcmInfo.IsSupported)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                this.logger.AddUserMessage(msg);
                DialogResult dialogResult = MessageBox.Show(msg, "Abort");
                return false;
            }

            if (!pcmInfo.IsSupportedRead)
            {
                string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                this.logger.AddUserMessage(msg);
                DialogResult dialogResult = MessageBox.Show(msg, "Abort");
                return false;
            }

            if (pcmInfo.HardwareType == PcmType.P05)
            {
                string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                this.logger.AddUserMessage(msg);
                DialogResult dialogResult = MessageBox.Show(msg, "Continue?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    this.logger.AddUserMessage("User chose not to proceed.");
                    return false;
                }
            }

            await this.vehicle.SuppressChatter();

            bool unlocked = await this.vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
            if (!unlocked)
            {
                this.logger.AddUserMessage("Unlock was not successful.");
                return false;
            }

            this.logger.AddUserMessage("Unlock succeeded.");

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
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
                return false;
            }

            // Save the contents to the path that the user provided.
            while(true)
            {
                try
                {
                    this.logger.AddUserMessage("Saving contents to " + path);

                    readResponse.Value.Position = 0;

                    using (Stream output = File.Open(path, FileMode.Create))
                    {
                        await readResponse.Value.CopyToAsync(output);
                    }

                    return true;
                }
                catch (IOException exception)
                {
                    this.logger.AddUserMessage("Unable to save file: " + exception.Message);
                    this.logger.AddDebugMessage(exception.ToString());

                    this.invoke(async () => path = await this.promptForFilePath());
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
    }
}

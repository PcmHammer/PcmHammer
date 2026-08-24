// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Writes the flash of a GM CAN PCM through its resident boot loader, which is the only path that
    /// can program a slave CPU. Unlocks, enters programming mode, then works through the download in
    /// phases: invalidate the SRAM parameter mirror, upload the master flash routines, program the
    /// master modules, then upload the slave flash routines and program the slave modules. Streaming
    /// the master OS module is what arms the slave, so the master phase always runs first.
    /// </summary>
    /// <remarks>
    /// The phases are the same for every boot loader; what varies is data on <see cref="OSIDInfo"/>. A
    /// phase whose data is empty is skipped, rather than being a separate code path.
    /// </remarks>
    public class CanBootLoaderWriter
    {
        // TransferData header: the service and mode bytes plus a four byte address.
        private const int TransferDataHeaderLength = 6;

        // Settling time between ReturnToNormal and the finalize. Without a gap the PCM is still busy
        // with the last module and ignores the finalize.
        private static readonly TimeSpan ReturnToNormalSettleTime = TimeSpan.FromMilliseconds(500);

        // Message factory. Stateless, so the wire format has one source of truth.
        private static readonly Gmlan gmlan = new Gmlan();

        private readonly Vehicle vehicle;
        private readonly CanCommands commands;
        private readonly OSIDInfo pcmInfo;
        private readonly ILogger logger;

        public CanBootLoaderWriter(Vehicle vehicle, CanCommands commands, OSIDInfo pcmInfo, ILogger logger)
        {
            this.vehicle = vehicle;
            this.commands = commands;
            this.pcmInfo = pcmInfo;
            this.logger = logger;
        }

        /// <summary>
        /// Program the master, and the slave when slave modules are supplied. Modules must be complete
        /// module images, as built by <see cref="FlashModuleBuilder"/>. The slave flash driver is null
        /// where the slave OS module carries its own flash routines.
        /// </summary>
        public async Task<bool> Write(
            byte[] masterFlashLibrary,
            IList<FlashModule> masterModules,
            byte[]? slaveFlashDriver,
            IList<FlashModule>? slaveModules,
            CancellationToken cancellationToken)
        {
            try
            {
                this.vehicle.ClearDeviceMessageQueue();

                List<DownloadPhase> phases = BuildPhases(
                    this.pcmInfo, masterFlashLibrary, masterModules, slaveFlashDriver, slaveModules);

                this.logger.StatusUpdateActivity("Unlocking PCM...");
                if (!await this.commands.Unlock(this.pcmInfo, cancellationToken))
                {
                    this.logger.AddUserMessage("Unable to unlock the PCM.");
                    return false;
                }

                this.logger.StatusUpdateActivity("Entering programming mode...");
                if (!await this.commands.EnterProgrammingMode(cancellationToken))
                {
                    return false;
                }

                this.logger.AddUserMessage("Programming the PCM through the boot loader.");
                this.logger.AddUserMessage("This takes several minutes. Do not interrupt it.");

                return await this.Write(phases, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Clean user-requested abort; the caller reports it, so don't imply a fault here.
                this.logger.AddDebugMessage("Write cancelled by user.");
                return false;
            }
            catch (Exception exception)
            {
                // On a requested stop, treat whatever surfaced as part of that clean abort.
                if (cancellationToken.IsCancellationRequested)
                {
                    this.logger.AddDebugMessage("Write cancelled by user: " + exception.Message);
                    return false;
                }

                this.logger.AddUserMessage("Something went wrong. " + exception.Message);
                this.logger.AddUserMessage("Do not power off the PCM! Do not exit this program!");
                this.logger.AddUserMessage("Try flashing again. If errors continue, seek help online.");
                this.logger.AddDebugMessage(exception.ToString());
                return false;
            }
            finally
            {
                this.logger.StatusUpdateReset();
            }
        }

        /// <summary>
        /// Send each phase in turn, reporting progress as the PCM accepts data and stopping at the
        /// first message it rejects.
        /// </summary>
        private async Task<bool> Write(List<DownloadPhase> phases, CancellationToken cancellationToken)
        {
            long totalBytes = 0;
            foreach (DownloadPhase phase in phases)
            {
                foreach (Message message in phase.Messages)
                {
                    totalBytes += DataByteCount(message.GetBytes());
                }
            }

            long bytesSent = 0;
            DateTime startTime = DateTime.Now;

            foreach (DownloadPhase phase in phases)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                this.logger.AddUserMessage(phase.Description);
                this.logger.StatusUpdateActivity(phase.Activity);

                foreach (Message message in phase.Messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    byte[] request = message.GetBytes();

                    if (IsReturnToNormal(request))
                    {
                        // Answered late, or not at all when the PCM resets on it, so send and move on.
                        await this.commands.SendBootLoaderNotification(message);
                        this.logger.AddDebugMessage(Describe(request) + " (no reply expected)");
                        await Task.Delay(ReturnToNormalSettleTime, cancellationToken);
                        continue;
                    }

                    Response<byte[]> response = await this.commands.SendBootLoaderMessage(message, cancellationToken);
                    if (response.Status != ResponseStatus.Success)
                    {
                        this.logger.AddUserMessage(string.Format(
                            "No response to {0}: {1}", Describe(request), response.Status));
                        return false;
                    }

                    if (!this.IsAccepted(request, response.Value))
                    {
                        return false;
                    }

                    bytesSent += DataByteCount(request);
                    this.ReportProgress(bytesSent, totalBytes, startTime);

                    // Control messages are logged individually; data messages are covered by the
                    // progress bar.
                    if (DataByteCount(request) == 0)
                    {
                        this.logger.AddDebugMessage(Describe(request) + " -> " + response.Value.ToHex());
                    }
                }
            }

            this.logger.AddUserMessage("Programming completed.");
            return true;
        }

        /// <summary>
        /// True when the PCM accepted a message. The master handshake reads the master OSID, which an
        /// erased or partly written master cannot report, so a rejection there is expected during a
        /// recovery write. Any other negative response ends the download.
        /// </summary>
        private bool IsAccepted(byte[] request, byte[] response)
        {
            bool rejected = response.Length >= 1 && response[0] == Gmlan.NegativeResponse;

            if (this.pcmInfo.BootLoaderMasterHandshakeDid != 0
                && IsHandshake(request, this.pcmInfo.BootLoaderMasterHandshakeDid))
            {
                if (rejected)
                {
                    this.logger.AddDebugMessage("The master did not report its OSID; its flash is erased or partly written.");
                }

                return true;
            }

            if (IsSlaveHandshake(request))
            {
                if (response.Length < 2
                    || response[0] != Gmlan.ReadDataByIdentifierResponse
                    || response[1] != request[1])
                {
                    this.logger.AddUserMessage("The slave CPU did not answer, so it cannot be programmed.");
                    return false;
                }

                return true;
            }

            if (rejected)
            {
                this.logger.AddUserMessage(string.Format(
                    "The boot loader rejected {0} with code 0x{1:X2}.",
                    Describe(request),
                    response.Length >= 3 ? response[2] : (byte)0));
                return false;
            }

            return true;
        }

        private void ReportProgress(long bytesSent, long totalBytes, DateTime startTime)
        {
            if (totalBytes <= 0)
            {
                return;
            }

            TimeSpan elapsed = DateTime.Now - startTime;
            double bytesPerSecond = elapsed.TotalSeconds > 0 ? bytesSent / elapsed.TotalSeconds : 0;
            string timeRemaining = bytesPerSecond > 0
                ? TimeSpan.FromSeconds((totalBytes - bytesSent) / bytesPerSecond).ToString("mm\\:ss")
                : string.Empty;

            this.logger.StatusUpdatePercentDone(string.Format("{0}%", bytesSent * 100 / totalBytes));
            this.logger.StatusUpdateTimeRemaining("T-" + timeRemaining);
            this.logger.StatusUpdateKbps(bytesPerSecond > 0 ? string.Format("{0:0.00} Kbps", bytesPerSecond * 8.00 / 1000.00) : string.Empty);
            this.logger.StatusUpdateProgressBar((double)bytesSent / totalBytes, true);
        }

        /// <summary>
        /// Build the whole download, in order. Pure, so a sequence can be generated and inspected
        /// without a PCM. Pass null or empty slave data for a master-only download.
        /// </summary>
        public static List<DownloadPhase> BuildPhases(
            OSIDInfo pcmInfo,
            byte[] masterFlashLibrary,
            IList<FlashModule>? masterModules,
            byte[]? slaveFlashDriver,
            IList<FlashModule>? slaveModules)
        {
            var phases = new List<DownloadPhase>();

            // Fill the SRAM parameter mirror with 0xFF, so the stale mirror is not saved back over the
            // new parameter flash when the PCM shuts down. The boot loader copies this to SRAM but
            // never runs it, and nothing later in the download reaches the validity marker at the
            // bottom of the range, so the fill stands until shutdown.
            if (pcmInfo.SramEraseLength > 0)
            {
                byte[] erased = new byte[pcmInfo.SramEraseLength];
                for (int i = 0; i < erased.Length; i++)
                {
                    erased[i] = 0xFF;
                }

                phases.Add(new DownloadPhase(
                    "Invalidating the NVM parameter mirror in SRAM.",
                    "Invalidating NVM mirror...",
                    BuildUploadMessages(pcmInfo, pcmInfo.SramEraseStart, erased)));
            }

            phases.Add(new DownloadPhase(
                "Uploading the master flash routines.",
                "Uploading flash routines...",
                BuildUploadMessages(pcmInfo, pcmInfo.BootLoaderLibraryAddress, masterFlashLibrary)));

            // Where a handshake starts the master burn it comes first, then the modules stream. The OS
            // module comes first and is what arms the slave.
            var masterMessages = new List<Message>();
            if (pcmInfo.BootLoaderMasterHandshakeDid != 0)
            {
                masterMessages.Add(gmlan.CreateReadByIdRequest(pcmInfo.BootLoaderMasterHandshakeDid));
            }

            if (masterModules != null)
            {
                foreach (FlashModule module in masterModules)
                {
                    masterMessages.AddRange(BuildModuleMessages(pcmInfo, module));
                }
            }

            phases.Add(new DownloadPhase("Programming the master flash.", "Programming master flash...", masterMessages));

            if (slaveModules != null && slaveModules.Count > 0)
            {
                var slaveMessages = new List<Message>();

                if (slaveFlashDriver != null)
                {
                    // Order matters: declare the driver, engage the armed slave, then relay the driver
                    // data. The handshake sits between the driver's RequestDownload and its TransferData.
                    slaveMessages.Add(gmlan.CreateRequestDownloadRequest(slaveFlashDriver.Length, 3));
                    AppendSlaveHandshake(slaveMessages, pcmInfo);
                    AppendTransferData(pcmInfo, slaveMessages, pcmInfo.BootLoaderLibraryAddress, slaveFlashDriver, incrementAddress: true);
                }
                else
                {
                    // No separate driver: the slave OS module carries its own flash routines, so there is
                    // no download for the handshake to sit inside and it leads the phase.
                    AppendSlaveHandshake(slaveMessages, pcmInfo);
                }

                foreach (FlashModule module in slaveModules)
                {
                    slaveMessages.AddRange(BuildModuleMessages(pcmInfo, module));
                }

                phases.Add(new DownloadPhase(
                    "Programming the slave CPU.", "Programming slave CPU...", slaveMessages));
            }

            // ReturnToNormal ends the programming session for every module on the bus, then DeviceControl
            // ends it for this PCM.
            phases.Add(new DownloadPhase(
                "Finalizing.",
                "Finalizing...",
                new List<Message>
                {
                    gmlan.CreateReturnToNormalRequest(),
                    gmlan.CreateBootLoaderFinalizeRequest(),
                }));

            return phases;
        }

        /// <summary>
        /// A flash routine upload or a RAM fill: RequestDownload declares the exact size, then the data
        /// streams to an incrementing address.
        /// </summary>
        private static List<Message> BuildUploadMessages(OSIDInfo pcmInfo, uint address, byte[] data)
        {
            var messages = new List<Message> { gmlan.CreateRequestDownloadRequest(data.Length, 3) };
            AppendTransferData(pcmInfo, messages, address, data, incrementAddress: true);
            return messages;
        }

        /// <summary>
        /// One module: RequestDownload declares the fixed block size and how the payload is coded, then
        /// every block streams to the staging address, which the boot loader consumes as it programs the
        /// flash its header names. A module with a header sends that header as a message of its own,
        /// because the boot loader parses it before the module data arrives and rejects a header merged
        /// into a larger message.
        /// </summary>
        private static List<Message> BuildModuleMessages(OSIDInfo pcmInfo, FlashModule module)
        {
            var messages = new List<Message>
            {
                gmlan.CreateRequestDownloadRequest(pcmInfo.BootLoaderBlockSize, 2, module.DataFormat)
            };

            int offset = 0;
            if (module.HeaderLength > 0 && module.HeaderLength < module.Data.Length)
            {
                messages.Add(TransferDataMessage(pcmInfo.BootLoaderStagingAddress, module.Data, 0, module.HeaderLength));
                offset = module.HeaderLength;
            }

            AppendTransferData(pcmInfo, messages, pcmInfo.BootLoaderStagingAddress, module.Data, incrementAddress: false, offset: offset);
            return messages;
        }

        private static void AppendSlaveHandshake(List<Message> messages, OSIDInfo pcmInfo)
        {
            foreach (byte did in pcmInfo.BootLoaderSlaveHandshakeDids)
            {
                messages.Add(gmlan.CreateReadByIdRequest(did));
            }
        }

        /// <summary>
        /// Split data from <paramref name="offset"/> onwards into TransferData messages of at most one
        /// block each.
        /// </summary>
        private static void AppendTransferData(
            OSIDInfo pcmInfo, List<Message> messages, uint address, byte[] data, bool incrementAddress, int offset = 0)
        {
            int maxDataBytes = pcmInfo.BootLoaderBlockSize - TransferDataHeaderLength;
            for (; offset < data.Length; offset += maxDataBytes)
            {
                int count = Math.Min(maxDataBytes, data.Length - offset);
                uint blockAddress = incrementAddress ? address + (uint)offset : address;
                messages.Add(TransferDataMessage(blockAddress, data, offset, count));
            }
        }

        private static Message TransferDataMessage(uint address, byte[] data, int offset, int count)
        {
            byte[] chunk = new byte[count];
            Buffer.BlockCopy(data, offset, chunk, 0, count);
            return gmlan.CreateNonExecChunkMessage(chunk, address);
        }

        private static bool IsHandshake(byte[] request, byte did) =>
            request.Length >= 2 && request[0] == Gmlan.ReadDataByIdentifier && request[1] == did;

        private static bool IsReturnToNormal(byte[] request) =>
            request.Length == 1 && request[0] == Gmlan.ReturnToNormalMode;

        private bool IsSlaveHandshake(byte[] request)
        {
            foreach (byte did in this.pcmInfo.BootLoaderSlaveHandshakeDids)
            {
                if (IsHandshake(request, did))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Payload bytes a message carries; anything but TransferData moves no data.</summary>
        private static int DataByteCount(byte[] message) =>
            message.Length > TransferDataHeaderLength && message[0] == Gmlan.TransferData
                ? message.Length - TransferDataHeaderLength
                : 0;

        private static string Describe(byte[] request)
        {
            if (request.Length == 0)
            {
                return "an empty message";
            }

            switch (request[0])
            {
                case Gmlan.RequestDownload:
                    return "RequestDownload";

                case Gmlan.TransferData:
                    uint address = request.Length >= TransferDataHeaderLength
                        ? ((uint)request[2] << 24) | ((uint)request[3] << 16) | ((uint)request[4] << 8) | request[5]
                        : 0;
                    return string.Format("TransferData to 0x{0:X6}", address);

                case Gmlan.ReadDataByIdentifier:
                    return string.Format("ReadDataByIdentifier 0x{0:X2}", request.Length > 1 ? request[1] : 0);

                case Gmlan.ReturnToNormalMode:
                    return "ReturnToNormal";

                case Gmlan.DeviceControl:
                    return "DeviceControl";

                default:
                    return string.Format("service 0x{0:X2}", request[0]);
            }
        }

        /// <summary>
        /// One phase of a download: what to report while it runs, and the messages it sends.
        /// </summary>
        public class DownloadPhase
        {
            public string Description { get; }

            public string Activity { get; }

            public IList<Message> Messages { get; }

            public DownloadPhase(string description, string activity, IList<Message> messages)
            {
                this.Description = description;
                this.Activity = activity;
                this.Messages = messages;
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    /// <remarks>
    /// Methods in this class are high-level operations like "get the VIN," or "read the contents of the EEPROM."
    /// </remarks>
    public partial class Vehicle : IDisposable
    {
        /// <summary>
        /// Suppres chatter on the VPW bus.
        /// </summary>
        public async Task SuppressChatter()
        {
            logger.AddDebugMessage("Suppressing VPW chatter.");
            Message suppressChatter = this.protocol.CreateDisableNormalMessageTransmission();
            await this.device.SendMessage(suppressChatter);
            await this.notifier.ForceNotify();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (stopwatch.ElapsedMilliseconds < 1000)
            {
                Message received = await this.device.ReceiveMessage();
                if (received != null)
                {
                    logger.AddDebugMessage("Ignoring chatter: " + received.ToString());
                    break;
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        }

        /// <summary>
        /// Writes a block of data to the PCM
        /// Requires an unlocked PCM
        /// </summary>
        private async Task<Response<bool>> WriteBlock(byte block, byte[] data)
        {
            Message m;
            Message ok = new Message(new byte[] { Priority.Physical0, DeviceId.Tool, DeviceId.Pcm, 0x7B, block });

            switch (data.Length)
            {
                case 6:
                    m = new Message(new byte[] { Priority.Physical0, DeviceId.Pcm, DeviceId.Tool, 0x3B, block, data[0], data[1], data[2], data[3], data[4], data[5] });
                    break;
                default:
                    logger.AddDebugMessage("Cant write block size " + data.Length);
                    return Response.Create(ResponseStatus.Error, false);
            }

            if (!await this.device.SendMessage(m))
            {
                logger.AddUserMessage("Failed to write block " + block + ", communications failure");
                return Response.Create(ResponseStatus.Error, false);
            }

            logger.AddDebugMessage("Successful write to block " + block);
            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Look for a kernel/loader payload embedded in the entry assembly. Single-file builds
        /// (e.g. the Linux CLI) bundle the kernels into the executable so it runs with no loose
        /// .bin files alongside it. Matches by file name, ignoring any namespace prefix in the
        /// manifest resource name. Returns null when no embedded copy is present (e.g. the Windows
        /// builds, which ship loose kernel files next to the exe).
        /// </summary>
        /// <summary>
        /// True when a manifest resource name refers to the requested kernel file. The build
        /// flattens resource names to the bare file name (LogicalName), but a namespace prefix
        /// such as "PcmHacking.Kernel-P01.bin" is also accepted. Deliberately not a plain
        /// EndsWith: that would let a request for "P01.bin" match "Kernel-P01.bin", and since the
        /// order of GetManifestResourceNames is undefined, a loose match could pick either of two
        /// candidates nondeterministically.
        /// </summary>
        private static bool IsResourceNameMatch(string resourceName, string fileName)
        {
            if (string.Equals(resourceName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return resourceName.Length > fileName.Length
                && resourceName[resourceName.Length - fileName.Length - 1] == '.'
                && resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);
        }

        private static byte[]? TryLoadEmbeddedKernel(string fileName)
        {
            System.Reflection.Assembly? assembly = System.Reflection.Assembly.GetEntryAssembly();
            if (assembly == null)
            {
                return null;
            }

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (IsResourceNameMatch(resourceName, fileName))
                {
                    using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            return null;
                        }

                        using (MemoryStream buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            return buffer.ToArray();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Opens the named kernel file. The file must be in the same directory as the EXE, in a
        /// Kernels subfolder of it, or embedded in the executable (single-file builds).
        /// </summary>
        public async Task<Response<byte[]>> LoadKernelFromFile(string path)
        {
            byte[] file = { 0x00 }; // dummy value

            if (path == "")
            {
                return Response.Create(ResponseStatus.Error, file);
            }
            string finalDir = string.Empty;
            if(_basePath == string.Empty)
            {
                // Not GetExecutingAssembly().Location: that is empty for the memory-loaded
                // PcmLibrary in the single-exe build, and Path.GetDirectoryName would then throw.
                finalDir = AppContext.BaseDirectory;
            }
            else
            {
                finalDir = _basePath;
            }

            // A loose file next to the exe takes precedence, so --kernel-dir (or a .bin dropped
            // there) can override the build. Otherwise look in the Kernels subfolder the build
            // deploys to.
            string subfolderPath = Path.Combine(finalDir, "Kernels", Path.GetFileName(path));
            path = Path.Combine(finalDir, path);
            if (!File.Exists(path) && File.Exists(subfolderPath))
            {
                path = subfolderPath;
            }

            // With no file either way, fall back to a copy embedded in the executable, which is how
            // the single-file build ships its kernels.
            if (!File.Exists(path))
            {
                byte[]? embedded = TryLoadEmbeddedKernel(Path.GetFileName(path));
                if (embedded != null && embedded.Length > 0)
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        string embHash = BitConverter.ToString(md5.ComputeHash(embedded)).Replace("-", "");
                        string embName = Path.GetFileName(path);
                        string embType = embName.StartsWith("Loader", StringComparison.OrdinalIgnoreCase) ? "Loader" : "Kernel";
                        logger.AddUserMessage($"Loaded {embName} ({embedded.Length} bytes, embedded)");
                        logger.AddUserMessage($"{embType} MD5={embHash}");
                    }

                    return Response.Create(ResponseStatus.Success, embedded);
                }
            }

            try
            {
                using (Stream fileStream = File.OpenRead(path))
                {
                    if (fileStream.Length == 0)
                    {
                        logger.AddDebugMessage("invalid kernel image (zero bytes). " + path);
                        return Response.Create(ResponseStatus.Error, file);
                    }
                    file = new byte[fileStream.Length];

                    // In theory we might need a loop here. In practice, I don't think that will be necessary.
                    int bytesRead = await fileStream.ReadAsync(file, 0, (int)fileStream.Length);

                    if(bytesRead != fileStream.Length)
                    {
                        return Response.Create(ResponseStatus.Truncated, file);
                    }
                }

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    string hash = BitConverter.ToString(md5.ComputeHash(file)).Replace("-", "");
                    string fileName = Path.GetFileName(path);
                    string payloadType = fileName.StartsWith("Loader", StringComparison.OrdinalIgnoreCase) ? "Loader" : "Kernel";
                    logger.AddUserMessage($"Loaded {fileName} ({file.Length} bytes)");
                    logger.AddUserMessage($"{payloadType} MD5={hash}");
                }
            }
            catch (ArgumentException)
            {
                logger.AddDebugMessage("Invalid file path " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (PathTooLongException)
            {
                logger.AddDebugMessage("File path is too long " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (DirectoryNotFoundException)
            {
                logger.AddDebugMessage("Invalid directory " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (IOException)
            {
                logger.AddDebugMessage("Error accessing file " + path);
                return Response.Create(ResponseStatus.Error, file);
            }
            catch (UnauthorizedAccessException)
            {
                logger.AddDebugMessage("No permission to read file " + path);
                return Response.Create(ResponseStatus.Error, file);
            }

            return Response.Create(ResponseStatus.Success, file);
        }

        /// <summary>
        /// Cleanup calls the various cleanup routines to get everything back to normal
        /// </summary>
        /// <remarks>
        /// Exit kernel at 4x, 1x, and clear DTCs
        /// </remarks>
        public async Task Cleanup()
        {
            // User-visible (not debug): this is the "back to normal" step every read/write/compare ends
            // with, so the log shows the kernel was halted and codes cleared. ClearTroubleCodes logs its
            // own "Clearing trouble codes." line. Mirrors the CAN path's messages.
            logger.AddUserMessage("Returning PCM to normal mode.");
            await this.ExitKernel();
            await this.ClearTroubleCodes();
        }

        /// <summary>
        /// Return the PCM to normal operation when the running kernel's bus is not known. The manual
        /// "Halt Running Kernel" button can be pressed at any time, so the kernel may be running on
        /// either bus. This sends mode 0x20 and then clears trouble codes on every bus the device can
        /// reach: CAN 500k first (if supported), then VPW at 4X (if supported) and 1X. A bus the
        /// device cannot use is simply skipped. Operations that already know the protocol use the
        /// protocol-specific cleanup instead (this.Cleanup for VPW, the CAN writer/reader for CAN).
        /// </summary>
        public async Task HaltKernel(CancellationToken cancellationToken)
        {
            // The kernel may be running on either bus, so halt and then clear codes on every bus the
            // device can reach. Each step logs once here; the per-bus calls below stay silent.
            logger.AddUserMessage("Halting kernel.");

            bool can = await this.device.SetProtocol(BusProtocol.Can500k);
            if (can)
            {
                this.SetTarget(Target.Pcm);
                await this.CreateCanCommands().Reboot(cancellationToken, announce: false);
            }

            bool vpw = await this.device.SetProtocol(BusProtocol.Vpw);
            if (vpw)
            {
                this.SetTarget(Target.Pcm);
                await this.ExitKernel();
            }

            logger.AddUserMessage("Clearing trouble codes.");

            if (can && await this.device.SetProtocol(BusProtocol.Can500k))
            {
                this.SetTarget(Target.Pcm);
                await this.CreateCanCommands().ClearDiagnosticCodes(cancellationToken, announce: false);
            }

            if (vpw && await this.device.SetProtocol(BusProtocol.Vpw))
            {
                this.SetTarget(Target.Pcm);
                await this.ClearTroubleCodes(announce: false);
            }
        }

        /// <summary>
        /// Exits the kernel at 4x, then at 1x. Once this function has been called the bus will be back at 1x.
        /// </summary>
        /// <remarks>
        /// Can be used to force exit the kernel, if requied. Does not attempt the 4x exit if not supported by the current device.
        /// </remarks>
        public async Task ExitKernel()
        {
            Message exitKernel = this.protocol.CreateExitKernel();

            this.device.ClearMessageQueue();
            if (device.Supports4X)
            {
                await device.SetVpwSpeed(VpwSpeed.FourX);
                await this.device.SendMessage(exitKernel);
                await device.SetVpwSpeed(VpwSpeed.Standard);
            }

            await this.device.SendMessage(exitKernel);
        }

        /// <summary>
        /// Ask the factory operating system to clear trouble codes. 
        /// In theory this should only run 10 seconds after rebooting, to ensure that the operating system is running again.
        /// In practice, that hasn't been an issue. It's the other modules (TAC especially) that really need to be reset.
        /// </summary>
        public async Task ClearTroubleCodes(bool announce = true)
        {
            if (announce)
            {
                logger.AddUserMessage("Clearing trouble codes.");
            }
            this.device.ClearMessageQueue();

            // No timeout because we don't care about responses to these messages.
            await this.device.SetTimeout(TimeoutScenario.Minimum);

            // The response is not checked because the priority byte and destination address are odd.
            // Different devices will handle this differently. Scantool won't recieve it.
            // so we send it twice just to be sure.
            Message clearCodesRequest = this.protocol.CreateClearDiagnosticTroubleCodesRequest();

            await Task.Delay(250);
            await this.device.SendMessage(clearCodesRequest);
            await Task.Delay(250);
            await this.device.SendMessage(clearCodesRequest);

            // This is a conventional message, but the response from the PCM might get lost 
            // among the responses from other modules on the bus, so again we just send it twice.
            Message clearDiagnosticInformationRequest = this.protocol.CreateClearDiagnosticInformationRequest();

            await Task.Delay(250);
            await this.device.SendMessage(clearDiagnosticInformationRequest);
            await Task.Delay(250);
            await this.device.SendMessage(clearDiagnosticInformationRequest);
        }

        /// <summary>
        /// Query the PCM's operating system ID.
        /// </summary>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryOperatingSystemIdFromKernel(CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.protocol.CreateOperatingSystemIdKernelRequest,
                this.protocol.ParseOperatingSystemIdKernelResponse,
                CancellationToken.None);

            return await query.Execute();
        }

        /// <summary>
        /// Ask the kernel for the ID of the flash chip.
        /// </summary>
        public async Task<Response<UInt32>> QueryFlashChipId(CancellationToken cancellationToken)
        {
            for (int retries = 0; retries < 3; retries++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (UInt32)0);
                }

                await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);
                Query<UInt32> chipIdQuery = this.CreateQuery<UInt32>(
                    this.protocol.CreateFlashMemoryTypeQuery,
                    this.protocol.ParseFlashMemoryType,
                    cancellationToken);
                Response<UInt32> chipIdResponse = await chipIdQuery.Execute();

                if (chipIdResponse.Status == ResponseStatus.Cancelled)
                {
                    return Response.Create(ResponseStatus.Cancelled, (UInt32)0);
                }

                if (chipIdResponse.Status != ResponseStatus.Success)
                {
                    continue;
                }

                if (chipIdResponse.Value == 0)
                {
                    continue;
                }

                return chipIdResponse;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Response.Create(ResponseStatus.Cancelled, (UInt32)0);
            }

            logger.AddUserMessage("Unable to determine which flash chip is in this PCM");
            return Response.Create(ResponseStatus.Error, (UInt32)0);
        }

        /// <summary>
        /// Ask the kernel whether the IAC driver chip is present.
        /// </summary>
        /// <remarks>
        /// Only the P01/P59 kernel answers this. The chip is probed over the QSPI bus.
        /// </remarks>
        public async Task<Response<ushort>> QueryIACDriver(CancellationToken cancellationToken)
        {
            for (int retries = 0; retries < 3; retries++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, (ushort)0);
                }

                await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);
                Query<ushort> iacQuery = this.CreateQuery<ushort>(
                    this.protocol.CreateDetectIacQuery,
                    this.protocol.ParseDetectIac,
                    cancellationToken);
                Response<ushort> iacResponse = await iacQuery.Execute();

                if (iacResponse.Status == ResponseStatus.Cancelled)
                {
                    return Response.Create(ResponseStatus.Cancelled, (ushort)0);
                }

                if (iacResponse.Status == ResponseStatus.Success)
                {
                    return iacResponse;
                }
            }

            return Response.Create(ResponseStatus.Error, (ushort)0);
        }

        /// <summary>
        /// Check for a running kernel.
        /// </summary>
        /// <returns></returns>
        public static string FormatKernelVersion(UInt64 v)
        {
            uint epoch = (uint)((v >> 8) & 0xFFFFFFFF);
            byte pcmType = (byte)(v & 0xFF);
            var dt = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            return $"{dt:yyyy-MM-dd HH:mm:ss} PCM=0x{pcmType:X2}";
        }

        /// <summary>
        /// Ask a running kernel for its version. Returns 0 if no kernel answers.
        /// </summary>
        /// <remarks>
        /// The token is deliberately required rather than optional. There used to be a convenience
        /// overload without one that forwarded CancellationToken.None; it read identically at the call
        /// site, so WriteManager picked it up by accident and that step of a write could not be
        /// cancelled at all. Making the token impossible to omit is what stops that recurring.
        /// </remarks>
        public async Task<UInt64> GetKernelVersion(CancellationToken cancellationToken, int maxRetries = 5)
        {
            Message query = this.protocol.CreateKernelVersionQuery();
            for (int retryCount = 0; retryCount < maxRetries; retryCount++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return 0;
                }

                if (!await this.device.SendMessage(query))
                {
                    await Task.Delay(100);
                    continue;
                }

                Message reply = await this.device.ReceiveMessage();
                if (reply == null)
                {
                    await Task.Delay(100);
                    continue;
                }

                Response<UInt64> response = this.protocol.ParseKernelVersion(reply);
                if ((response.Status == ResponseStatus.Success) && (response.Value != 0))
                {
                    return response.Value;
                }

                if (response.Status == ResponseStatus.Refused)
                {
                    return 0;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return 0;
                }

                await Task.Delay(100);
            }

            return 0;
        }

        /// <summary>
        /// Load the executable payload on the PCM at the supplied address, and execute it.
        /// </summary>
        public async Task<bool> PCMExecute(OSIDInfo info, byte[] payload, CancellationToken cancellationToken)
        {
            // Note that we request an upload of 4k maximum, because the PCM will reject anything bigger.
            // But you can request a 4k upload and then send up to 16k if you want, and the PCM will not object.
            int claimedSize = Math.Min(4096, payload.Length);

            // Since we're going to lie about the size, we need to check for overflow ourselves.
            // TODO: Can we just use the real size?
            if (info.HardwareType == PcmType.P01 || info.HardwareType == PcmType.P59)
            {
                if (info.KernelBaseAddress + payload.Length > 0xFFCDFF)
                {
                    logger.AddUserMessage("Base address and size would exceed usable RAM.");
                    return false;
                }
            }

            int loadAddress;

            if (info.LoaderRequired)
            {
                loadAddress = info.LoaderBaseAddress;
                logger.AddUserMessage("PCM uses a kernel loader.");
            }
            else
            {
                loadAddress = info.KernelBaseAddress;
            }

            logger.AddDebugMessage($"Sending upload request for {(info.LoaderRequired ? "loader" : "kernel")} size {payload.Length}, loadaddress {loadAddress.ToString("X6")}");
            logger.AddUserMessage("Requesting upload permission.");

            Query<bool> uploadPermissionQuery = new Query<bool>(
                this.device,
                () => protocol.CreateUploadRequest(info, claimedSize),
                (message) => protocol.ParseUploadPermissionResponse(info, message),
                this.logger,
                cancellationToken,
                this.notifier);

            Response<bool> permissionResponse = await uploadPermissionQuery.Execute();
            bool uploadAllowed = permissionResponse.Status == ResponseStatus.Success && permissionResponse.Value;

            if (!uploadAllowed)
            {
                logger.AddUserMessage(
                    $"Permission to upload {(info.LoaderRequired ? "Loader" : "Kernel")} was denied." +
                    Environment.NewLine +
                    "This usually means communication started before the PCM finished its power-on security delay. " +
                    "Cut power to the PCM, restore power, wait about 10 seconds, then try again."
                    );
                return false;
            }

            logger.AddUserMessage("Upload permission granted.");
            logger.AddDebugMessage($"Going to load a {payload.Length} byte {(info.LoaderRequired ? "loader" : "kernel")} to 0x{loadAddress.ToString("X6")}");

            await this.device.SetTimeout(TimeoutScenario.SendKernel);

            // Loop through the payload building and sending packets, highest first, execute on last
            int payloadSize = device.MaxKernelSendSize - 12; // Headers use 10 bytes, sum uses 2 bytes.
            if (info.LoaderBaseAddress > 0 && loadAddress == info.KernelBaseAddress)
            {
                payloadSize = 512;  // If we are using a loader kernel use a small packet size due to limited resources.
            }
            int chunkCount = payload.Length / payloadSize;
            int remainder = payload.Length % payloadSize;

            int offset = (chunkCount * payloadSize);
            int startAddress = loadAddress + offset;

            // First we send the 'remainder' payload, containing any bytes that won't fill up an entire upload packet.
            logger.AddDebugMessage($"Sending end block payload with offset 0x{offset:X}, start address 0x{startAddress:X}, length 0x{remainder:X}.");

            Message remainderMessage = protocol.CreateBlockMessage(
                payload, 
                offset, 
                remainder,
                loadAddress + offset, 
                remainder == payload.Length ? BlockCopyType.Execute : BlockCopyType.Copy);

            await notifier.Notify();
            Response<bool> uploadResponse = await WritePayload(remainderMessage, cancellationToken);
            if (uploadResponse.Status != ResponseStatus.Success)
            {
                logger.AddDebugMessage($"Could not upload {(info.LoaderRequired ? "loader" : "kernel")} to PCM, remainder payload not accepted.");
                return false;
            }

            // Now we send a series of full upload packets
            // Note that there's a notifier.Notify() call inside the WritePayload() call in this loop.
            for (int chunkIndex = chunkCount; chunkIndex > 0; chunkIndex--)
            {
                int bytesSent = payload.Length - offset;
                int percentDone = bytesSent * 100 / payload.Length;

                logger.AddUserMessage($"{(info.LoaderRequired ? "Loader" : "Kernel")} upload {percentDone}% complete.");

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                offset = (chunkIndex - 1) * payloadSize;
                startAddress = loadAddress + offset;

                Message payloadMessage = protocol.CreateBlockMessage(
                    payload,
                    offset,
                    payloadSize,
                    startAddress,
                    offset == 0 ? BlockCopyType.Execute : BlockCopyType.Copy);

                logger.AddDebugMessage($"Sending block with offset 0x{offset:X6}, start address 0x{startAddress:X6}, length 0x{payloadSize:X4}.");

                uploadResponse = await WritePayload(payloadMessage, cancellationToken);
                if (uploadResponse.Status != ResponseStatus.Success)
                {
                    logger.AddDebugMessage($"Could not upload {(info.LoaderRequired ? "loader" : "kernel")} to PCM, payload not accepted.");
                    return false;
                }
            }

            logger.AddUserMessage($"{(info.LoaderRequired ? "Loader" : "Kernel")} upload 100% complete.");

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (ReportKernelID && info.KernelVersionSupport)
            {
                UInt64 kernelVersion = await this.GetKernelVersion(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (kernelVersion == 0)
                {
                    logger.AddUserMessage($"{(info.LoaderRequired ? "Loader" : "Kernel")} failed to start.");
                    return false;
                }
                logger.AddUserMessage($"{(info.LoaderRequired ? "Loader" : "Kernel")} Version: {FormatKernelVersion(kernelVersion)}");
            }

            if (info.LoaderRequired)
            {
                // Switch modes to Kernel, Loader is already on PCM.
                // It has outlived it's usefulness, so use it for Loader vs Kernel state switch.
                info.LoaderRequired = false;
            }

            return true;
        }

        /// <summary>
        /// Does everything required to switch to VPW 4x
        /// </summary>
        public async Task<bool> VehicleSetVPW4x(OSIDInfo pcmInfo, VpwSpeed newSpeed)
        {
            if (!device.Supports4X) 
            {
                if (newSpeed == VpwSpeed.FourX)
                {
                    // where there is no support only report no switch to 4x
                    logger.AddUserMessage("This interface does not support VPW 4x");
                }
                return true;
            }

            if ((newSpeed == VpwSpeed.FourX) && !this.device.Enable4xReadWrite)
            {
                logger.AddUserMessage("4X communications disabled by configuration.");
                return true;
            }
            // FIXME: This should be in a common library, not here
            // OBDX Pro VT should be 1x for the P04 due to P04 VPW bus load not being high enough when
            // on the bench. The bus load is OK in the car, but car modules wake up and cause bus to
            // crash to 1x regardless.
            if (pcmInfo.HardwareType == PcmType.P04 || pcmInfo.HardwareType == PcmType.P04_Early)
            {
                if (this.device.ToString().Contains("OBDX Pro VT"))
                {
                    logger.AddUserMessage("OBDXPro VT with P04 PCM detected. Reducing interface speed to 1x. Reinit interface to restore 4x");
                    this.device.Enable4xReadWrite = false;
                    return true;
                }
            }

            // Configure the vehicle bus when switching to 4x
            if (newSpeed == VpwSpeed.FourX)
            {
                logger.AddUserMessage("Attempting switch to VPW 4x");
                await device.SetTimeout(TimeoutScenario.ReadProperty);

                // The list of modules may not be useful after all, but 
                // checking for an empty list indicates an uncooperative
                // module on the VPW bus.
                List<byte>? modules = await this.RequestHighSpeedPermission(notifier);
                if (modules == null)
                {
                    // A device has refused the switch to high speed mode.
                    return false;
                }

                // Since we had some issue with other modules not staying quiet...
                await this.ForceSendToolPresentNotification();

                Message broadcast = this.protocol.CreateBeginHighSpeed(DeviceId.Broadcast);
                await this.device.SendMessage(broadcast);

                // Check for any devices that refused to switch to 4X speed.
                // These responses usually get lost, so this code might be pointless.
                // Like RequestHighSpeedPermission above, this listens directly on the device
                // rather than via Query<T>: the refusals can come from any module on the bus,
                // and Query<T>'s inbound filter would drop everything except replies from a
                // single addressed module.
                Stopwatch sw = new Stopwatch();
                sw.Start();

                // WARNING: The AllPro stopped receiving permission-to-upload messages when this timeout period
                // was set to 1500ms.  Reducing it to 500 seems to have fixed that problem.
                //
                // It would be nice to find a way to wait equally long with all devices, as refusal messages
                // are still a potetial source of trouble.
                Message? response = null;
                while (((response = await this.device.ReceiveMessage()) != null) && (sw.ElapsedMilliseconds < 500))
                {
                    Response<bool> refused = this.protocol.ParseHighSpeedRefusal(response);
                    if (refused.Status != ResponseStatus.Success)
                    {
                        // This should help ELM devices receive responses.
                        await Task.Delay(100);
                        await notifier.ForceNotify();
                        continue;
                    }

                    if (refused.Value == false)
                    {
                        // TODO: Add module number.
                        logger.AddUserMessage("Module refused high-speed switch.");
                        return false;
                    }
                }
            }
            else
            {
                logger.AddUserMessage("Reverting to VPW 1x");
            }

            // Request the device to change
            await device.SetVpwSpeed(newSpeed);

            // Since we had some issue with other modules not staying quiet...
            await this.ForceSendToolPresentNotification();

            return true;
        }

        /// <summary>
        /// Ask all of the devices on the VPW bus for permission to switch to 4X speed.
        /// </summary>
        private async Task<List<byte>?> RequestHighSpeedPermission(ToolPresentNotifier notifier)
        {
            // This is a broadcast exchange: every module on the bus may answer, and a single
            // refusal must abort the switch. It deliberately drives the device directly rather
            // than through Query<T>, because Query<T> installs an inbound filter that keeps only
            // replies from the one module it addressed. That filter would discard the other
            // modules' grant/refuse responses we specifically need to collect here.
            Message permissionCheck = this.protocol.CreateHighSpeedPermissionRequest(DeviceId.Broadcast);
            await this.device.SendMessage(permissionCheck);

            // Note that as of right now, the AllPro only receives 6 of the 11 responses.
            // So until that gets fixed, we could miss a 'refuse' response and try to switch
            // to 4X anyhow. That just results in an aborted read attempt, with no harm done.
            List<byte> result = new List<byte>();
            Message? response = null;
            bool anyRefused = false;
            while ((response = await this.device.ReceiveMessage()) != null)
            {
                logger.AddDebugMessage("Parsing " + response.GetBytes().ToHex());
                Protocol.HighSpeedPermissionResult parsed = this.protocol.ParseHighSpeedPermissionResponse(response);
                if (!parsed.IsValid)
                {
                    await Task.Delay(100);
                    continue;
                }

                result.Add(parsed.DeviceId);

                if (parsed.PermissionGranted)
                {
                    logger.AddUserMessage(string.Format("Module 0x{0:X2} ({1}) has agreed to enter high-speed mode.", parsed.DeviceId, DeviceId.DeviceCategory(parsed.DeviceId)));

                    // Forcing a notification message should help ELM devices receive responses.
                    await notifier.ForceNotify();
                    await Task.Delay(100);
                    continue;
                }

                logger.AddUserMessage(string.Format("Module 0x{0:X2} ({1}) has refused to enter high-speed mode.", parsed.DeviceId, DeviceId.DeviceCategory(parsed.DeviceId)));
                anyRefused = true;
            }

            if (anyRefused)
            {
                return null;
            }

            return result;
        }

        /// <summary>
        /// Sends the provided message, with a retry loop. 
        /// </summary>
        public async Task<Response<bool>> WritePayload(Message message, CancellationToken cancellationToken)
        {
            int retryCount = 0;
            for (; retryCount < MaxSendAttempts; retryCount++)
            {
                await this.notifier.Notify();

                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, false, retryCount);
                }

                await Task.Delay(50); // Allow the running kernel time to enter the ReadMessage function

                if (!await device.SendMessage(message))
                {
                    logger.AddDebugMessage("WritePayload: Unable to send message.");
                    continue;
                }

                if (await WaitForSuccess(this.protocol.ParseUploadResponse, cancellationToken, request: message))
                {
                    return Response.Create(ResponseStatus.Success, true, retryCount);
                }

                logger.AddDebugMessage("WritePayload: Upload request failed.");
                await Task.Delay(100);
                await this.SendToolPresentNotification();
            }

            logger.AddDebugMessage("WritePayload: Giving up.");
            return Response.Create(ResponseStatus.Error, false, retryCount);
        }
    }
}

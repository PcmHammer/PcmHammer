// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// GMLAN/CAN command set for GM CAN PCMs. Messages are built and parsed by <see cref="Gmlan"/>.
    /// Every exchange runs through <see cref="Query{T}"/>, which clears the device queue before each
    /// send and loops on the parser's result: Success ends the exchange, Error aborts, anything else
    /// keeps reading. Response-pending (7F .. 78) keepalives are handled once, generically, in
    /// <see cref="Device.ReceiveMessage"/>, so no parser here needs to special-case them.
    /// </summary>
    public class CanCommands : ISecurityAccess
    {
        /// <summary>Selects the GMLAN security key table.</summary>
        public BusProtocol Bus => BusProtocol.Can500k;

        // Upper bound on a write block's code size, kept under a small interface's CAN buffer
        // (e.g. an OBDLink). The boot loader imposes no alignment.
        private const int TransferPageSize = 0x400;

        private static readonly TimeSpan SecurityDelayLockout = TimeSpan.FromSeconds(11);

        /// <summary>Seed and key length, in bytes, for PCMs with 40-bit external security.</summary>
        private const int SeedKeyLength40 = 5;

        private readonly Device device;
        private readonly Gmlan gmlan;
        private readonly ILogger logger;

        // A user-supplied unlock key (0x0000-0xFFFF), or -1 when none is set. Used instead of the
        // computed key to recover a PCM with corrupt security data.
        private readonly int userDefinedKey;

        // Supplies the key for PCMs with external 40-bit security (e.g. E92); null when the host
        // offers no interactive key entry. Proven pairs are cached in keyStore for reuse.
        private readonly SecurityKeyProvider? securityKeyProvider;
        private readonly SecurityKeyStore keyStore = SecurityKeyStore.Default();

        public CanCommands(Device device, ILogger logger, int userDefinedKey = -1, SecurityKeyProvider? securityKeyProvider = null)
        {
            this.device = device;
            this.gmlan = new Gmlan();
            this.logger = logger;
            this.userDefinedKey = userDefinedKey;
            this.securityKeyProvider = securityKeyProvider;
        }

        // Inbound filters. The device already filters to the target's response id; these drop the
        // device's transmit-echo frame (4-byte tx CAN id, leads with 0x00) and stray frames left from
        // a previous exchange so they never reach a parser or burn the receive budget. AcceptAll is
        // for handshakes whose parsers read past the echo themselves.
        private static readonly Func<Message, Predicate<Message>> AcceptAll = _ => (m => true);

        // A real GMLAN/UDS response never leads with 0x00.
        private static readonly Func<Message, Predicate<Message>> AcceptResponses = _ => (m =>
        {
            byte[] bytes = m?.GetBytes() ?? Array.Empty<byte>();
            return bytes.Length > 0 && bytes[0] != 0x00;
        });

        // Mode 0x3D replies are 0x7D (positive) or 0x7F (negative) only.
        private static readonly Func<Message, Predicate<Message>> AcceptMode3D = _ => (m =>
        {
            byte[] bytes = m?.GetBytes() ?? Array.Empty<byte>();
            return bytes.Length > 0 && (bytes[0] == Gmlan.Mode3DResponse || bytes[0] == Gmlan.NegativeResponse);
        });

        // Flash-write replies are the 0x76 ack or 0x7F only.
        private static readonly Func<Message, Predicate<Message>> AcceptWriteAck = _ => (m =>
        {
            byte[] bytes = m?.GetBytes() ?? Array.Empty<byte>();
            return bytes.Length > 0 && (bytes[0] == Gmlan.NonExecChunkAck || bytes[0] == Gmlan.NegativeResponse);
        });

        // ---- Unlock (security access 0x27) --------------------------------------------------------

        /// <summary>
        /// Seed/key unlock (0x27/01 then 0x27/02). GM CAN PCMs grant security without an extended
        /// session first. Seed 0x0000 means already unlocked; NRC 0x37 is a timed lockout.
        /// </summary>
        public async Task<bool> Unlock(OSIDInfo pcmInfo, CancellationToken cancellationToken)
        {
            if (pcmInfo.Uses40BitSecurity)
            {
                return await this.Unlock40Bit(pcmInfo, cancellationToken);
            }

            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            for (int seedAttempt = 1; seedAttempt <= 2; seedAttempt++)
            {
                Response<ushort> seed = await this.RequestSeed(cancellationToken);

                bool waitAndRetry;
                if (!this.SeedIsUsable(seed.Status, seedAttempt, out waitAndRetry))
                {
                    if (waitAndRetry)
                    {
                        await Task.Delay(SecurityDelayLockout, cancellationToken);
                        continue;
                    }
                    return false;
                }

                // 0x0000/0xFFFF usually means corrupt security data; suggest a user key.
                if (((seed.Value == 0x0000) || (seed.Value == 0xFFFF)) && (this.userDefinedKey == -1))
                {
                    this.logger.AddUserMessage($"***NOTICE**** Seed is 0x{seed.Value:X4}, if this process fails, try setting a user defined key of 0x{seed.Value:X4}");
                }

                // Seed 0x0000 = already unlocked. With a user key set, still send it (corrupt-param recovery).
                if ((seed.Value == 0x0000) && (this.userDefinedKey == -1))
                {
                    this.logger.AddUserMessage("Already unlocked (seed 0x0000).");
                    return true;
                }

                ushort key;
                if (this.userDefinedKey == -1)
                {
                    key = KeyAlgorithm.GetKey(pcmInfo.BusProtocol, pcmInfo.KeyAlgorithm, seed.Value);
                }
                else
                {
                    this.logger.AddUserMessage($"User Defined Key: 0x{this.userDefinedKey:X4}");
                    key = (ushort)this.userDefinedKey;
                }

                this.logger.AddDebugMessage(string.Format("seed=0x{0:X4} key=0x{1:X4}", seed.Value, key));

                Query<bool> unlock = this.MakeQuery(
                    () => this.gmlan.CreateUnlockRequest(key),
                    this.gmlan.ParseUnlockResponse,
                    cancellationToken,
                    maxTimeouts: 5);
                Response<bool> result = await unlock.Execute();
                if (result.Status == ResponseStatus.Success && result.Value)
                {
                    return true;
                }

                this.logger.AddUserMessage("Unlock rejected (" + result.Status + ").");
                return false;
            }

            return false;
        }

        /// <summary>
        /// Unlock a PCM with external 40-bit security (e.g. E92). Request the 5-byte seed, show it,
        /// use a saved key for this PCM type + seed if we have one, otherwise ask the host's key
        /// provider (which prompts the user). A key the PCM accepts is cached for next time; a
        /// rejected one is forgotten. The key algorithm itself lives outside this app.
        /// </summary>
        private async Task<bool> Unlock40Bit(OSIDInfo pcmInfo, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            for (int seedAttempt = 1; seedAttempt <= 2; seedAttempt++)
            {
                Response<byte[]> seed = await this.RequestSeed40(cancellationToken);

                bool waitAndRetry;
                if (!this.SeedIsUsable(seed.Status, seedAttempt, out waitAndRetry))
                {
                    if (waitAndRetry)
                    {
                        await Task.Delay(SecurityDelayLockout, cancellationToken);
                        continue;
                    }
                    return false;
                }

                // All-zero seed means the PCM is already unlocked.
                if (Utility.IsAllBytes(seed.Value, 0, seed.Value.Length, 0x00))
                {
                    this.logger.AddUserMessage("Already unlocked (seed is all zero).");
                    return true;
                }

                string seedHex = seed.Value.ToHex(string.Empty);
                this.logger.AddUserMessage("Security seed: " + seedHex);

                byte[]? key = this.keyStore.TryGet(pcmInfo.HardwareType, seed.Value);
                bool fromUser = false;
                if (key != null)
                {
                    this.logger.AddUserMessage("Using saved key for this seed.");
                }
                else if (this.securityKeyProvider == null)
                {
                    this.logger.AddUserMessage(
                        "No saved key for this seed and no interactive key entry is available. " +
                        "Compute the key from the seed above and provide it, then retry.");
                    return false;
                }
                else
                {
                    // The prompt blocks this operation, so do not raise one for an operation the user
                    // has already cancelled.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    key = this.securityKeyProvider(pcmInfo.HardwareType, seed.Value, cancellationToken);
                    fromUser = true;
                }

                if (key == null || key.Length != SeedKeyLength40)
                {
                    // Declining the prompt, or cancelling while it is up, is not an error worth reporting.
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        this.logger.AddUserMessage("No valid 5-byte key was provided.");
                    }
                    return false;
                }

                this.logger.AddDebugMessage("seed=" + seedHex + " key=" + key.ToHex(string.Empty));

                Query<bool> unlock = this.MakeQuery(
                    () => this.gmlan.CreateUnlockRequest40(key),
                    this.gmlan.ParseUnlockResponse,
                    cancellationToken,
                    maxTimeouts: 5);
                Response<bool> result = await unlock.Execute();
                if (result.Status == ResponseStatus.Success && result.Value)
                {
                    string? saveError = this.keyStore.Save(pcmInfo.HardwareType, seed.Value, key);
                    if (fromUser)
                    {
                        this.logger.AddUserMessage(saveError == null
                            ? "Key accepted and saved for next time."
                            : "Key accepted, but it could not be saved: " + saveError);
                    }
                    return true;
                }

                // A rejected key must not stay cached.
                this.keyStore.Remove(pcmInfo.HardwareType, seed.Value);
                this.logger.AddUserMessage("Unlock rejected (" + result.Status + ").");
                return false;
            }

            return false;
        }

        /// <summary>
        /// Common handling of a seed response for both unlock paths. Returns true when the seed can be
        /// used; otherwise <paramref name="waitAndRetry"/> says whether the caller should wait out a
        /// security lockout (NRC 0x37) and ask again, or give up.
        /// </summary>
        private bool SeedIsUsable(ResponseStatus status, int seedAttempt, out bool waitAndRetry)
        {
            waitAndRetry = false;

            if (status == ResponseStatus.Refused)
            {
                // Lockout (NRC 0x37): wait out the delay once, then retry.
                if (seedAttempt == 1)
                {
                    this.logger.AddUserMessage("Security lockout, waiting to retry.");
                    waitAndRetry = true;
                    return false;
                }

                this.logger.AddUserMessage("Still in security lockout.");
                return false;
            }

            if (status != ResponseStatus.Success)
            {
                // Cancellation is not a failure; let the caller's notice stand alone.
                if (status != ResponseStatus.Cancelled)
                {
                    this.logger.AddUserMessage("No seed response (" + status + ").");
                }
                return false;
            }

            return true;
        }

        private Task<Response<byte[]>> RequestSeed40(CancellationToken cancellationToken)
            => this.SeedQuery(this.gmlan.ParseSeed40, Array.Empty<byte>(), cancellationToken);

        private Task<Response<ushort>> RequestSeed(CancellationToken cancellationToken)
            => this.SeedQuery(this.gmlan.ParseSeed, (ushort)0, cancellationToken);

        /// <summary>
        /// Request a security seed (0x27 0x01) and parse it with <paramref name="parser"/>: 16-bit for
        /// most PCMs, 40-bit for those with external security. A timed lockout (NRC 0x37) is reported as
        /// Refused so the caller can wait it out and retry.
        /// </summary>
        private async Task<Response<T>> SeedQuery<T>(Func<Message, Response<T>> parser, T refusedValue, CancellationToken cancellationToken)
        {
            Query<T> query = new Query<T>(
                this.device,
                () => this.gmlan.CreateSeedRequest(),
                (message) =>
                {
                    byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
                    if (bytes.Length >= 3 && bytes[0] == Gmlan.NegativeResponse && bytes[2] == Gmlan.NrcSecurityDelay)
                        return Response.Create(ResponseStatus.Refused, refusedValue);
                    return parser(message!);
                },
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptAll);
            query.MaxTimeouts = 5;
            return await query.Execute();
        }

        // ---- Brute-force security access (ISecurityAccess) ----------------------------------------
        // Single seed request / single key attempt reporting the raw outcome; the BruteForcer owns the
        // loop and timing.

        /// <summary>Request a single security-access seed.</summary>
        public async Task<BruteForceSeedResult> RequestSeedForBruteForce(CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.Detect);
            Query<BruteForceSeedResult> query = new Query<BruteForceSeedResult>(
                this.device,
                () => this.gmlan.CreateSeedRequest(),
                (message) =>
                {
                    byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
                    // Lockout (7F 27 37): Locked, without consuming a key attempt.
                    if (bytes.Length >= 3 && bytes[0] == Gmlan.NegativeResponse && bytes[2] == Gmlan.NrcSecurityDelay)
                        return Response.Create(ResponseStatus.Success, BruteForceSeedResult.Locked());
                    // Seed: 67 01 hi lo.
                    if (bytes.Length >= 4 && bytes[0] == Gmlan.SecurityAccessResponse && bytes[1] == 0x01)
                        return Response.Create(ResponseStatus.Success, BruteForceSeedResult.Ok((ushort)((bytes[2] << 8) | bytes[3])));
                    return Response.Create(ResponseStatus.UnexpectedResponse, default(BruteForceSeedResult));
                },
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptResponses);
            query.MaxTimeouts = 2;
            Response<BruteForceSeedResult> result = await query.Execute();
            return result.Status == ResponseStatus.Success ? result.Value : BruteForceSeedResult.Failure();
        }

        /// <summary>Tester-present (0x3E) to keep the session alive. Best effort; any reply is left
        /// for the next query's queue clear.</summary>
        public async Task SendKeepAlive(CancellationToken cancellationToken)
        {
            try
            {
                await this.device.SendMessage(this.gmlan.CreateTesterPresentRequest());
            }
            catch
            {
                // A missed keep-alive is not fatal.
            }
        }

        /// <summary>Send a single candidate key and classify the PCM's response.</summary>
        public async Task<SecurityUnlockResult> SendKeyForBruteForce(UInt16 key, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.Detect);
            Query<SecurityUnlockResult> query = new Query<SecurityUnlockResult>(
                this.device,
                () => this.gmlan.CreateUnlockRequest(key),
                (message) =>
                {
                    byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
                    if (bytes.Length >= 2 && bytes[0] == Gmlan.SecurityAccessResponse && bytes[1] == 0x02)
                        return Response.Create(ResponseStatus.Success, SecurityUnlockResult.Unlocked);
                    if (bytes.Length >= 3 && bytes[0] == Gmlan.NegativeResponse && bytes[1] == 0x27)
                    {
                        switch (bytes[2])
                        {
                            case 0x35: return Response.Create(ResponseStatus.Success, SecurityUnlockResult.InvalidKey);        // Invalid Key
                            case 0x36: return Response.Create(ResponseStatus.Success, SecurityUnlockResult.TooManyAttempts);   // Exceeded attempts
                            case 0x37: return Response.Create(ResponseStatus.Success, SecurityUnlockResult.TimeDelayActive);   // Required time delay
                            case 0x33: return Response.Create(ResponseStatus.Success, SecurityUnlockResult.Denied);            // Security access denied
                            default: return Response.Create(ResponseStatus.Success, SecurityUnlockResult.Unexpected);
                        }
                    }
                    return Response.Create(ResponseStatus.UnexpectedResponse, SecurityUnlockResult.NoResponse);
                },
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptResponses);
            query.MaxTimeouts = 2;
            Response<SecurityUnlockResult> result = await query.Execute();
            return result.Status == ResponseStatus.Success ? result.Value : SecurityUnlockResult.NoResponse;
        }

        // ---- Kernel upload ------------------------------------------------------------------------

        /// <summary>
        /// Put the PCM in programming mode, which every download starts with. PCM must already be
        /// unlocked. Sequence: 0x10/02 ProgrammingSession (optional), 0x28 DisableNormalCommunication
        /// (optional), tester present, 0xA5/01 ProgrammingMode (required), 0xA5/03 (optional).
        /// </summary>
        public async Task<bool> EnterProgrammingMode(CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            // ProgrammingSession (0x10 0x02). The factory sequence enters this session before the
            // upload; it puts the OS's CAN comms in the state the launched kernel expects (so the kernel's
            // re-armed RX mailbox actually matches). Optional/best-effort: answer is 0x50.
            await this.MakeQuery(
                () => this.gmlan.CreateProgrammingSessionRequest(),
                this.Confirm(b => b[0] == 0x50),
                cancellationToken, maxTimeouts: 2).Execute();

            // Optional: some PCMs answer 0x68/0x60, others reject it; programming mode is tried next regardless.
            await this.MakeQuery(
                () => this.gmlan.CreateDisableNormalCommunicationRequest(),
                this.Confirm(b => b[0] == 0x68 || b[0] == 0x60),
                cancellationToken, maxTimeouts: 2).Execute();

            await this.device.SendMessage(this.gmlan.CreateTesterPresentRequest());

            this.logger.AddUserMessage("Requesting programming mode.");
            Response<bool> progMode = await this.MakeQuery(
                () => this.gmlan.CreateProgrammingModeRequest(),
                this.gmlan.ParseProgrammingModeResponse,
                cancellationToken, maxTimeouts: 5).Execute();
            if (progMode.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("Programming mode rejected (" + progMode.Status + ").");
                return false;
            }

            // 0xA5/03 is optional and usually unanswered; one quick attempt to keep the session alive.
            await this.MakeQuery(
                () => this.gmlan.CreateProgrammingModeStep3Request(),
                this.gmlan.ParseProgrammingModeResponse,
                cancellationToken, maxTimeouts: 1).Execute();

            return true;
        }

        /// <summary>
        /// Enter programming mode and upload the kernel, confirming it launches. PCM must already be
        /// unlocked. Sends the 0x34 RequestDownload and 0x36 transfer blocks built by the PCM's
        /// <see cref="CanKernelUploadProtocol"/> (the boot loader dialect).
        /// </summary>
        public async Task<bool> UploadKernel(OSIDInfo pcmInfo, byte[] payload, CancellationToken cancellationToken)
        {
            CanKernelUploadProtocol protocol = CanKernelUploadProtocol.For(pcmInfo.GMLANProtocol);
            uint loadAddress = (uint)pcmInfo.KernelBaseAddress;
            uint runAddress = (uint)pcmInfo.KernelRunAddress;

            if (!await this.EnterProgrammingMode(cancellationToken))
            {
                return false;
            }

            CanKernelUpload upload = protocol.BuildUpload(
                this.gmlan, payload, loadAddress, runAddress, this.device.MaxCanKernelBlockSize);

            // Total framed bytes actually put on the wire, for progress (distinct from the size the
            // RequestDownload declares, which is the dialect's business).
            int bytesToSend = 0;
            foreach (CanUploadBlock block in upload.Blocks)
            {
                bytesToSend += block.Message.GetBytes().Length;
            }

            // Long receive window for the whole download: the executing block's ack only comes once the
            // kernel has launched (a few hundred ms). Any 7F..78 "pending" keepalives are swallowed
            // generically in Device.ReceiveMessage.
            await this.device.SetTimeout(TimeoutScenario.ReadCrc);

            this.logger.AddDebugMessage(string.Format(
                "RequestDownload, then {0} block(s), {1} bytes.", upload.Blocks.Count, bytesToSend));
            Response<bool> reqDownload = await this.MakeQuery(
                () => upload.RequestDownload,
                this.gmlan.ParseRequestDownloadResponse,
                cancellationToken, maxTimeouts: 5).Execute();
            if (reqDownload.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("RequestDownload refused (" + reqDownload.Status + ").");
                return false;
            }

            // Blocks in send order; the last is the executing block. Copies are acked 0x76; the executing
            // block's ack is the boot loader's dialect.
            string kernelOrLoader = pcmInfo.LoaderRequired ? "Loader" : "Kernel";
            int sent = 0;
            for (int index = 0; index < upload.Blocks.Count; index++)
            {
                CanUploadBlock block = upload.Blocks[index];
                bool isExec = block.IsExecuting;
                this.logger.AddDebugMessage(string.Format(
                    "Uploading block {0}/{1} ({2} bytes, {3}).",
                    index + 1, upload.Blocks.Count, block.Message.GetBytes().Length, isExec ? "execute" : "copy"));

                Response<bool> ack = await this.MakeQuery(
                    () => block.Message,
                    m => this.IsBlockAck(m, protocol, isExec) ? Response.Create(ResponseStatus.Success, true) : Response.Create(ResponseStatus.UnexpectedResponse, false),
                    cancellationToken, maxTimeouts: isExec ? 8 : 5).Execute();
                if (ack.Status != ResponseStatus.Success)
                {
                    // The kernel announces its launch with a single frame that can be lost in the boot
                    // transient (it re-inits the CAN controller just before sending it). Before failing
                    // the upload, probe the kernel directly - if it answers, it is running and we only
                    // missed the announce. Copy-block acks are not subject to this, so they fail hard.
                    if (isExec && await this.KernelRespondsToProbe(cancellationToken))
                    {
                        this.logger.AddDebugMessage("Launch announce missed; kernel answered a probe, treating as running.");
                    }
                    else
                    {
                        this.logger.AddUserMessage(string.Format(
                            "Block {0}/{1} not acknowledged ({2}).",
                            index + 1, upload.Blocks.Count, isExec ? "kernel-launch ack" : "0x76 copy ack"));
                        return false;
                    }
                }

                sent += block.Message.GetBytes().Length;
                int percentDone = bytesToSend > 0 ? (sent * 100) / bytesToSend : 100;
                this.logger.AddUserMessage(string.Format("{0} upload {1}% complete.", kernelOrLoader, percentDone));
            }

            this.logger.AddDebugMessage("Kernel is running.");
            return true;
        }

        // Copy blocks are acked 0x76; the executing block's ack is the boot loader's dialect.
        private bool IsBlockAck(Message message, CanKernelUploadProtocol protocol, bool isExec)
        {
            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
            return isExec
                ? protocol.IsExecutingBlockAck(bytes)
                : (bytes.Length > 0 && bytes[0] == Gmlan.NonExecChunkAck);
        }

        // Confirm the kernel is actually running when its launch announce was missed: a running kernel
        // answers the version query (mode 0x3D 0x00); the stock boot loader does not.
        private async Task<bool> KernelRespondsToProbe(CancellationToken cancellationToken)
        {
            Response<ulong> version = await this.GetKernelVersion(cancellationToken);
            return version.Status == ResponseStatus.Success;
        }

        // ---- Post-kernel memory read --------------------------------------------------------------

        /// <summary>
        /// Read one KernelBlockSize (0x400) block: send 0x35, kernel acks 0x75 then streams the 0x36
        /// data block. The inbound filter drops the 0x75 ack so the parser only sees the data block.
        /// </summary>
        public async Task<Response<byte[]>> ReadMemoryBlock(uint address, int length, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadMemoryBlock);
            Query<byte[]> query = new Query<byte[]>(
                this.device,
                () => this.gmlan.CreateMemoryReadRequest(address, length),
                m => this.gmlan.ParseMemoryBlock(m, length),
                this.logger, cancellationToken, notifier: null,
                acceptInbound: _ => (m =>
                {
                    byte[] bytes = m?.GetBytes() ?? Array.Empty<byte>();
                    return bytes.Length > 0 && bytes[0] == Gmlan.MemoryBlockResponse;
                }));
            query.MaxTimeouts = 5;
            return await query.Execute();
        }

        // ---- Kernel queries (mode 0x3D) -----------------------------------------------------------

        /// <summary>
        /// Kernel version, packed as (epoch &lt;&lt; 8) | pcmType - the same value and format as the VPW
        /// path (<see cref="Vehicle.FormatKernelVersion"/>), so every PCM's version string carries the
        /// PCM type byte.
        /// </summary>
        public Task<Response<ulong>> GetKernelVersion(CancellationToken cancellationToken)
            => this.KernelQuery(() => this.gmlan.CreateKernelVersionRequest(), this.gmlan.ParseKernelVersionResponse, cancellationToken);

        public Task<Response<uint>> GetFlashId(CancellationToken cancellationToken)
            => this.KernelQuery(() => this.gmlan.CreateFlashIdRequest(), this.gmlan.ParseFlashIdResponse, cancellationToken);

        public Task<Response<uint>> GetCanStatus(CancellationToken cancellationToken)
            => this.KernelQuery(() => this.gmlan.CreateCanStatusRequest(), this.gmlan.ParseCanStatusResponse, cancellationToken);

        /// <summary>CRC-32 of [address, address+length) from the kernel. A large range can take several
        /// seconds, so allow more receive cycles.</summary>
        public Task<Response<uint>> GetRangeCrc(uint address, uint length, CancellationToken cancellationToken)
            => this.KernelQuery(() => this.gmlan.CreateCrcRequest(address, length), this.gmlan.ParseCrcResponse, cancellationToken, maxTimeouts: 8);

        // Kernel info/CRC replies are not instant (compute + watchdog), so use the long ReadCrc timeout.
        private async Task<Response<T>> KernelQuery<T>(Func<Message> generator, Func<Message, Response<T>> parser, CancellationToken cancellationToken, int maxTimeouts = 5)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadCrc);
            Query<T> query = new Query<T>(this.device, generator, parser, this.logger, cancellationToken, notifier: null, acceptInbound: AcceptMode3D);
            query.MaxTimeouts = maxTimeouts;
            return await query.Execute();
        }

        // ---- ReadDataByIdentifier (properties, GMLAN 0x1A) ----------------------------------------

        /// <summary>
        /// Read one DID (GMLAN 0x1A). Returns the full response bytes - positive [0x5A, did, data...]
        /// or negative [0x7F, 0x1A, nrc] - both as Success so the caller can format the value or NRC.
        /// </summary>
        public async Task<Response<byte[]>> ReadDataByIdentifier(byte did, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.Detect);
            Query<byte[]> query = new Query<byte[]>(
                this.device,
                () => this.gmlan.CreateReadByIdRequest(did),
                (message) =>
                {
                    byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
                    if (bytes.Length >= 2 && bytes[0] == Gmlan.ReadDataByIdentifierResponse && bytes[1] == did)
                        return Response.Create(ResponseStatus.Success, bytes);
                    if (bytes.Length >= 2 && bytes[0] == Gmlan.NegativeResponse && bytes[1] == Gmlan.ReadDataByIdentifier)
                        return Response.Create(ResponseStatus.Success, bytes);   // negative, for NRC display
                    return Response.Create(ResponseStatus.UnexpectedResponse, Array.Empty<byte>());
                },
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptResponses);
            query.MaxTimeouts = 2;
            return await query.Execute();
        }

        /// <summary>
        /// Write one DID (GMLAN 0x3B); e.g. 3B 90 + 17 VIN bytes. PCM must already be unlocked. Returns
        /// true on the positive response (7B did).
        /// </summary>
        public async Task<bool> WriteDataByIdentifier(byte did, byte[] data, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadCrc);
            Query<bool> query = new Query<bool>(
                this.device,
                () => this.gmlan.CreateWriteByIdRequest(did, data),
                message => this.gmlan.ParseWriteByIdResponse(message, did),
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptResponses);
            query.MaxTimeouts = 3;
            Response<bool> result = await query.Execute();
            return result.Status == ResponseStatus.Success && result.Value;
        }

        // ---- Flash erase / write ------------------------------------------------------------------

        /// <summary>Per-block write attempts before giving up.</summary>
        private const int MaxWriteAttempts = 5;

        /// <summary>
        /// Largest flash-write payload in one 0x36 block: device CAN capacity minus the write-block
        /// framing (7-byte header + 2-byte sum), capped at the page size to keep the kernel's reassembly
        /// buffer small.
        /// </summary>
        public int MaxFlashWriteBlockSize =>
            Math.Min(this.device.MaxCanKernelBlockSize - (Gmlan.WriteBlockHeaderLength + 2), TransferPageSize);

        /// <summary>
        /// Erase the flash sector containing <paramref name="address"/> (GMLAN 0x3D 0x05); reply is
        /// 0x7D 0x05 [status] (0x00 = success). Long erase timeout: a 64 KiB sector takes ~1 s.
        /// </summary>
        public async Task<Response<byte>> EraseFlashSector(uint address, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.EraseMemoryBlock);
            Query<byte> query = new Query<byte>(
                this.device,
                () => this.gmlan.CreateFlashEraseRequest(address),
                this.gmlan.ParseFlashEraseResponse,
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptMode3D);
            query.MaxTimeouts = 3;
            return await query.Execute();
        }

        /// <summary>
        /// Write one block to flash; when <paramref name="isTest"/>, the kernel validates and acks but
        /// does not erase/program. Owns its retry loop so RetryCount feeds the "retries" reporting.
        /// Kernel acks 0x76 on success.
        /// </summary>
        public async Task<Response<bool>> WriteFlashBlock(byte[] image, int offset, int length, uint address, bool isTest, CancellationToken cancellationToken)
        {
            // Long receive window: validating/programming a block on a small interface runs well past
            // the short WriteMemoryBlock window. Query still returns the instant the 0x76 ack arrives,
            // so this only prevents a premature timeout re-sending the block (which would leave a
            // duplicate ack and desync the next exchange).
            await this.device.SetTimeout(TimeoutScenario.ReadCrc);
            BlockCopyType copyType = isTest ? BlockCopyType.TestWrite : BlockCopyType.Copy;
            int retryCount = 0;

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, false, retryCount);
                }

                Query<bool> query = new Query<bool>(
                    this.device,
                    () => this.gmlan.CreateWriteBlockMessage(image, offset, length, address, copyType),
                    this.gmlan.ParseWriteBlockResponse,
                    this.logger, cancellationToken, notifier: null, acceptInbound: AcceptWriteAck);
                query.MaxTimeouts = 1;   // one send per attempt; this loop owns the retries
                Response<bool> result = await query.Execute();
                if (result.Status == ResponseStatus.Success && result.Value)
                {
                    return Response.Create(ResponseStatus.Success, true, retryCount);
                }

                retryCount++;
            }

            return Response.Create(ResponseStatus.Error, false, retryCount);
        }

        // ---- Reboot -------------------------------------------------------------------------------

        /// <summary>
        /// ReturnToNormalMode (0x20)
        /// </summary>
        public async Task<bool> Reboot(CancellationToken cancellationToken, bool announce = true)
        {
            if (announce)
            {
                this.logger.AddUserMessage("Returning to normal mode.");
            }

            await this.device.SetTimeout(TimeoutScenario.Detect);

            await this.MakeQuery(
                () => this.gmlan.CreateReturnToNormalRequest(),
                this.Confirm(b => b[0] == 0x60 || b[0] == 0x98),
                cancellationToken, maxTimeouts: 3).Execute();

            return true;
        }

        /// <summary>
        /// Clear the trouble codes the kernel session provokes (other modules log "lost communication"
        /// while the bus is busy). OBD-II ClearDiagnostics (0x04) on the functional broadcast id so all
        /// modules clear. Best-effort, repeated because the PCM is still restarting; never throws.
        /// </summary>
        public async Task ClearDiagnosticCodes(CancellationToken cancellationToken, bool announce = true)
        {
            if (!(this.device is ICanTarget target))
            {
                return; // not a CAN-capable device
            }

            if (announce)
            {
                logger.AddUserMessage("Clearing trouble codes.");
            }

            uint savedTx = target.TxCanId;
            uint savedRx = target.RxCanId;
            try
            {
                // Functional broadcast (0x7DF) so all modules clear; the replies are ignored.
                target.TxCanId = CanId.OBD2Functional;
                await this.device.SetTimeout(TimeoutScenario.Detect);

                Message clear = this.gmlan.CreateClearDiagnosticsRequest();
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    this.device.ClearMessageQueue();
                    await this.device.SendMessage(clear);
                    await Task.Delay(250, cancellationToken);
                }
            }
            catch
            {
                // Best-effort: a failed DTC clear must not turn a successful read into a failure.
            }
            finally
            {
                target.TxCanId = savedTx;
                target.RxCanId = savedRx;
            }
        }

        // ---- Helpers ------------------------------------------------------------------------------

        private Query<bool> MakeQuery(Func<Message> generator, Func<Message, Response<bool>> parser, CancellationToken cancellationToken, int maxTimeouts)
        {
            Query<bool> query = new Query<bool>(this.device, generator, parser, this.logger, cancellationToken, notifier: null, acceptInbound: AcceptAll);
            query.MaxTimeouts = maxTimeouts;
            return query;
        }

        // Positive per predicate; 7F is a hard error, anything else keeps reading. (Response-pending
        // 7F..78 keepalives are handled generically in Device.ReceiveMessage and never reach here.)
        private Func<Message, Response<bool>> Confirm(Func<byte[], bool> isPositive)
            => (message) =>
            {
                byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
                if (bytes.Length < 1) return Response.Create(ResponseStatus.UnexpectedResponse, false);
                if (isPositive(bytes)) return Response.Create(ResponseStatus.Success, true);
                if (bytes[0] == Gmlan.NegativeResponse)
                {
                    return Response.Create(ResponseStatus.Error, false);
                }
                return Response.Create(ResponseStatus.UnexpectedResponse, false);
            };

        // ---- Boot loader download -----------------------------------------------------------------

        /// <summary>
        /// Send one message of a boot loader download and return the reply. Long receive window: the
        /// boot loader programs flash as the download streams, so a message can take a while to answer.
        /// </summary>
        public async Task<Response<byte[]>> SendBootLoaderMessage(Message message, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.EraseMemoryBlock);

            Query<byte[]> query = new Query<byte[]>(
                this.device,
                () => message,
                m =>
                {
                    byte[] bytes = m?.GetBytes() ?? Array.Empty<byte>();

                    // A flash burn can send more response-pending keepalives than Device.ReceiveMessage
                    // waits through, so keep reading when one reaches this far.
                    if (bytes.Length >= 3 && bytes[0] == Gmlan.NegativeResponse && bytes[2] == Gmlan.NrcResponsePending)
                    {
                        return Response.Create(ResponseStatus.UnexpectedResponse, (byte[])null!);
                    }

                    return bytes.Length > 0
                        ? Response.Create(ResponseStatus.Success, bytes)
                        : Response.Create(ResponseStatus.UnexpectedResponse, (byte[])null!);
                },
                this.logger, cancellationToken, notifier: null, acceptInbound: AcceptResponses);
            query.MaxTimeouts = 8;
            return await query.Execute();
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
        /// How many times we should attempt to send a message before giving up.
        /// </summary>
        public const int MaxSendAttempts = 10;

        /// <summary>
        /// How many times we should attempt to receive a message before giving up.
        /// </summary>
        /// <remarks>
        /// 10 is too small for the case when we get a bunch of "chatter 
        /// suppressed" messages right before trying to upload the kernel.
        /// Might be worth making this a parameter to the retry loops since
        /// in most cases when only need about 5.
        /// </remarks>
        public const int MaxReceiveAttempts = 5;

        /// <summary>
        /// How long to wait for a PCM security time-delay lockout (response 0x37) to clear before
        /// re-requesting a seed during an unlock. Sized to cover one ~10s forced-delay window plus a
        /// small margin.
        /// </summary>
        private static readonly TimeSpan SecurityDelayLockout = TimeSpan.FromSeconds(11);

        public CancellationTokenSource ShutdownSignalSource = new CancellationTokenSource(); // Use this as a trigger to say we are ready to dispose the underlying device.

        /// <summary>
        /// The device we'll use to talk to the PCM.
        /// </summary>
        private Device device;

        /// <summary>
        /// This class knows how to generate message to send to the PCM.
        /// </summary>
        private Protocol protocol;

        /// <summary>
        /// This is how we send user-friendly status messages and developer-oriented debug messages to the UI.
        /// </summary>
        private ILogger logger;

        private readonly string _basePath;

        /// <summary>
        /// Use this to periodically send tool-present messages during long operations, to 
        /// discourage devices on the VPW bus from sending messages that could interfere
        /// with whatever the application is doing.
        /// </summary>
        private ToolPresentNotifier notifier;

        /// <summary>
        /// Gets a string that describes the device this instance is using.
        /// </summary>
        public string DeviceDescription
        {
            get
            {
                return this.device.ToString();
            }
        }

        public int DeviceMaxFlashWriteSendSize
        {
            get
            {
                return this.device.MaxFlashWriteSendSize;
            }
        }

        public int DeviceMaxReceiveSize
        {
            get
            {
                return this.device.MaxReceiveSize;
            }
        }

        public bool Supports4X
        {
            get => this.device.Supports4X;
        }

        /// <summary>Buses the current device can passively monitor.</summary>
        public IReadOnlyList<BusProtocol> MonitorableProtocols => this.device.MonitorableProtocols;

        /// <summary>Create a bus monitor over the current device.</summary>
        public BusMonitor CreateBusMonitor() => new BusMonitor(this.device, this.logger);

        public bool Enable4xReadWrite
        {
            set
            {
                this.device.Enable4xReadWrite = value;
            }

            get => this.device.Enable4xReadWrite;
        }

        public Int32 UserDefinedKey
        {
            get; set;
        } = -1;

        /// <summary>
        /// Silences Kernel ID reporting
        /// </summary>
        /// <remarks>
        /// See note Vehicle.Kernel PCMExecute(...)
        /// </remarks>
        public bool ReportKernelID
        {
            get; set;
        } = true;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Vehicle(
            Device device,
            Protocol protocol,
            ILogger logger,
            ToolPresentNotifier notifier,
            string basePath)
        {
            this.device = device;
            this.protocol = protocol;
            this.logger = logger;
            this.notifier = notifier;
            _basePath = basePath;
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~Vehicle()
        {
            _ = this.Dispose(false);
        }

        /// <summary>
        /// Implements IDisposable.Dispose.
        /// </summary>
        public void Dispose()
        {
            _ = this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Part of the Dispose pattern.
        /// </summary>
        protected async Task Dispose(bool isDisposing)
        {
            if (ShutdownSignalSource.IsCancellationRequested) // Prevent the disposal of the Vehicle class from disposing the device. This can then be held by ConnectionService to be passed back in.
            { 
                this.device.Dispose();
                this.device = null!;
            }
        }

        /// <summary>
        /// Re-initialize the device.
        /// </summary>
        public async Task<bool> ResetConnection()
        {
            Task<bool> task = this.device.Initialize();
            bool completedWithoutTimeout = await task.AwaitWithTimeout(TimeSpan.FromSeconds(10));
            if (!completedWithoutTimeout)
            {
                // Initialize() is still running (e.g. a defunct port that opens but never answers).
                // Do NOT read task.Result here: on an incomplete Task that blocks the caller until
                // the task finishes, and because Initialize()'s continuations resume on the calling
                // (often UI) thread, that block deadlocks the whole app. Report failure and leave the
                // orphaned task to unwind on its own. The caller disposes the device/port afterwards.
                return false;
            }

            return task.Result;
        }

        /// <summary>
        /// Send a tool-present notfication.  (Or not, depending on how much 
        /// time has passed since the last notificationw was sent.)
        /// </summary>
        /// <returns></returns>
        public async Task SendToolPresentNotification()
        {
            if (!this.device.Supports4X && (this.device.MaxFlashWriteSendSize > 600 || this.device.MaxReceiveSize > 600))
            {
                await this.notifier.ForceNotify();
            }
            else
            {
                await this.notifier.Notify();
            }
        }

        /// <summary>
        /// Send a tool-present notfication.  (Or not, depending on how much 
        /// time has passed since the last notificationw was sent.)
        /// </summary>
        /// <returns></returns>
        public async Task ForceSendToolPresentNotification()
        {
            await this.notifier.ForceNotify();
        }

        /// <summary>
        /// Change the device's timeout.
        /// </summary>
        public async Task<TimeoutScenario> SetDeviceTimeout(TimeoutScenario scenario)
        {
            return await this.device.SetTimeout(scenario);
        }

        /// <summary>
        /// Clear the device's incoming-message queue.
        /// </summary>
        public void ClearDeviceMessageQueue()
        {
            this.device.ClearMessageQueue();
        }

        /// <summary>
        /// Query factory. One could argue that this is in the wrong place.
        /// </summary>
        public Query<T> CreateQuery<T>(
            Func<Message> generator,
            Func<Message, Response<T>> parser,
            CancellationToken cancellationToken)
        {
            return new Query<T>(
                this.device,
                generator,
                parser,
                this.logger,
                cancellationToken,
                this.notifier);
        }

        /// <summary>
        /// Open an inbound message-filter scope for a request/response exchange driven
        /// through this Vehicle (e.g. the CKernel* read/verify loops, which don't hold a
        /// direct Device reference). While the returned scope is held, bus traffic that
        /// isn't a reply to the given request is dropped; dispose it when the exchange is
        /// done. Mirrors how the Query class filters its own exchanges.
        /// </summary>
        public IDisposable FilterRepliesTo(Message request)
        {
            return this.device.FilterInbound(MessageFilters.RepliesFrom(request));
        }

        public async Task<bool> SendMessage(Message message)
        {
            return await this.device.SendMessage(message);
        }

        public async Task<Message> ReceiveMessage()
        {
            return await this.device.ReceiveMessage();
        }


        // TODO (message filtering, 1c): verify recovery-mode detection still works with
        // inbound filtering active. These listens have no preceding request to derive a
        // filter from and run directly on the device (not via Query<T>), so they should be
        // unaffected — but confirm on hardware that an unsolicited recovery broadcast is
        // still seen once the filter feature is in use.
        public async Task<Response<bool>> CheckForRecoveryMode(CancellationToken cancellationToken)
        {
            bool result = await this.device.IsCommandBroadcasting(0xA2);
            if (result)
            {
                return Response.Create(ResponseStatus.Success, result);
            }
            return Response.Create(ResponseStatus.Success, false);
        }


        /// <summary>
        /// Unlock the PCM by requesting a 'seed' and then sending the corresponding 'key' value.
        /// </summary>
        public async Task<bool> UnlockEcu(int keyAlgorithm)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            Message seedRequest = this.protocol.CreateSeedRequest();

            // Seed and key are both Tool<->PCM exchanges, so one inbound filter (replies from
            // the PCM, addressed to the tool) covers the whole unlock. The scope spans every
            // seed re-request and security-delay wait, and is released the moment this method
            // returns (success, denial, or give-up) so a failed/aborted unlock leaves no filter
            // behind. The brute-force tool has its own bespoke seed/key loop in BruteForcer and
            // is intentionally left unfiltered.
            using (this.device.FilterInbound(MessageFilters.RepliesFrom(seedRequest)))
            {

            // The PCM permits a couple of key attempts and then forces a time delay before each
            // further attempt. A wrong key or an outright denial will never succeed, so we fail fast on
            // those; "too many attempts / time delay not expired" is recoverable, but the PCM requires
            // us to wait and start the whole exchange over from a fresh seed. Bound how many times we
            // start over so a permanently locked PCM still reports failure.
            const int MaxUnlockAttempts = 3;

            // Disambiguates the 67 01 37 seed response below; must persist across unlock attempts.
            bool keyAttempted = false;

            for (int unlockAttempt = 1; unlockAttempt <= MaxUnlockAttempts; unlockAttempt++)
            {
                this.device.ClearMessageQueue();

                logger.AddDebugMessage("Sending seed request.");

                bool seedReceived = false;
                UInt16 seedValue = 0;
                bool lockoutRetried = false;

                // (Re)send the seed request and listen for the answer, mirroring how Query<T> drives
                // every other request: resend if the PCM doesn't reply, and use tool-present pings to
                // keep slow PCMs (e.g. the Black Box) awake while we wait, instead of giving up on the
                // first timeout. A one-off security time-delay lockout is also ridden out here (wait +
                // retry once). Query<T> resends just once; allow a little more margin here, but not so
                // much that a genuinely dead bus takes a long time to report failure.
                const int MaxSeedRequests = 3;
                for (int sendAttempt = 1; (sendAttempt <= MaxSeedRequests) && !seedReceived; sendAttempt++)
                {
                    if (!await this.TrySendMessage(seedRequest, "seed request"))
                    {
                        logger.AddUserMessage("Unable to send seed request.");
                        return false;
                    }

                    bool lockoutDetected = false;
                    int timeouts = 0;

                    // Read up to 50 times (just to avoid looping forever) but only tolerate
                    // MaxReceiveAttempts timeouts before resending the request.
                    for (int receiveAttempt = 1; receiveAttempt <= 50; receiveAttempt++)
                    {
                        Message seedResponse = await this.device.ReceiveMessage();
                        if (seedResponse == null)
                        {
                            timeouts++;
                            if (timeouts >= MaxReceiveAttempts)
                            {
                                logger.AddDebugMessage(
                                    $"No response to seed request. Attempt #{receiveAttempt}, Timeout #{timeouts}.");
                                break;
                            }

                            // Keep the PCM awake and listen again rather than giving up.
                            await this.notifier.ForceNotify();
                            continue;
                        }

                        byte[] seedBytes = seedResponse.GetBytes();

                        // 67 01 37 means both "already unlocked" and "seed refused, delay still
                        // active" - same bytes, only context tells them apart. A delay can only exist
                        // after a rejected key, so before any key this is "already unlocked". A truly
                        // locked first request is still caught later when upload permission is refused.
			// TODO: This works, but does not sound right. Investigate more deeply and update
			// comment and/or logic.
                        if (this.protocol.IsSecurityDelayActive(seedBytes))
                        {
                            if (!keyAttempted)
                            {
                                logger.AddUserMessage("PCM is already unlocked.");
                                return true;
                            }

                            lockoutDetected = true;
                            break;
                        }

                        logger.AddDebugMessage("Parsing seed value.");
                        Response<UInt16> seedValueResponse = this.protocol.ParseSeed(seedBytes);
                        if (seedValueResponse.Status == ResponseStatus.Success)
                        {
                            seedValue = seedValueResponse.Value;
                            seedReceived = true;
                            break;
                        }

                        logger.AddDebugMessage("Unable to parse seed response. Attempt #" + receiveAttempt.ToString());
                    }

                    if (seedReceived)
                    {
                        break;
                    }

                    if (lockoutDetected)
                    {
                        if (lockoutRetried)
                        {
                            logger.AddUserMessage("PCM is still in a security time-delay lockout; unable to unlock.");
                            return false;
                        }

                        // Normal: the PCM rate-limits security access and is counting down a forced delay.
                        // Wait it out and re-request the seed once so the unlock can still succeed. (Note:
                        // probing security during the power-on lockout can leave some PCMs refusing the
                        // kernel upload for the rest of the power cycle - that case is handled where the
                        // upload-permission request is rejected, not here.) Don't charge this against the
                        // send-attempt budget.
                        lockoutRetried = true;
                        sendAttempt--;
                        logger.AddUserMessage("PCM is in a security time-delay lockout. Waiting to retry.");
                        await Task.Delay(SecurityDelayLockout);
                    }

                    // No usable seed this round; clear anything stale and let the loop resend.
                    this.device.ClearMessageQueue();
                }

                if (!seedReceived)
                {
                    logger.AddUserMessage("No seed reponse received, PCM not communicating.");
                    return false;
                }

                // If the seed is a common occurance of corrupted security data, and the user is not attempting to use a custom key, provide a useful suggestion
                if (((seedValue == 0x0000) || (seedValue == 0xFFFF)) && (UserDefinedKey == -1))
                {
                    logger.AddUserMessage($"***NOTICE**** Seed is 0x{seedValue.ToString("X4")}, if this process fails, try setting a user defined key of 0x{seedValue.ToString("X4")}");
                }

                // if we have a user defined key the user might be trying to recover from a corrupted param block
                // so we still let it though
                if ((seedValue == 0x0000) && (UserDefinedKey == -1))
                {
                    logger.AddUserMessage("PCM Unlock not required");
                    return true;
                }

                UInt16 key;
                if (UserDefinedKey == -1)
                {
                    key = KeyAlgorithm.GetKey(keyAlgorithm, seedValue);
                }
                else
                {
                    logger.AddUserMessage($"User Defined Key: 0x{UserDefinedKey.ToString("X4")}");
                    key = (UInt16)UserDefinedKey;
                }

                logger.AddDebugMessage("Sending unlock request (" + seedValue.ToString("X4") + ", " + key.ToString("X4") + ")");
                keyAttempted = true;
                Message unlockRequest = this.protocol.CreateUnlockRequest(key);
                if (!await this.TrySendMessage(unlockRequest, "unlock request"))
                {
                    logger.AddDebugMessage("Unable to send unlock request.");
                    return false;
                }

                bool retryAfterDelay = false;
                for (int attempt = 1; attempt < MaxReceiveAttempts; attempt++)
                {
                    Message unlockResponse = await this.device.ReceiveMessage();
                    if (unlockResponse == null)
                    {
                        logger.AddDebugMessage("No response to unlock request. Attempt #" + attempt.ToString());
                        continue;
                    }

                    byte[] unlockBytes = unlockResponse.GetBytes();
                    Response<bool> result = this.protocol.ParseUnlockResponse(unlockBytes, out string errorMessage);
                    if (errorMessage == null)
                    {
                        return result.Value;
                    }

                    logger.AddUserMessage(errorMessage);

                    // Classify the response, but only when it is a well-formed sendKey reply (so a short
                    // or unrelated message can neither be misread nor index past the end of the array).
                    byte unlockCode =
                        (unlockBytes.Length >= 6 &&
                         unlockBytes[3] == (Mode.Seed + Mode.Response) &&
                         unlockBytes[4] == Submode.SendKey)
                        ? unlockBytes[5]
                        : (byte)0x00;

                    // A wrong key (0x35) or an outright denial (0x33) will never unlock - stop now.
                    if (unlockCode == 0x35 || unlockCode == 0x33)
                    {
                        return false;
                    }

                    // Too many attempts (0x36) or time-delay-not-expired (0x37): the key was not
                    // evaluated. Wait the forced delay and start the exchange over with a fresh seed.
                    if (unlockCode == 0x36 || unlockCode == 0x37)
                    {
                        retryAfterDelay = true;
                        break;
                    }

                    // Anything else: keep listening for the real reply.
                }

                if (!retryAfterDelay || unlockAttempt >= MaxUnlockAttempts)
                {
                    break;
                }

                logger.AddUserMessage("PCM is enforcing a security time delay. Waiting to retry.");
                await Task.Delay(SecurityDelayLockout);
            }

            logger.AddUserMessage("Unable to process unlock response.");
            return false;
            } // using (FilterInbound)
        }

        /// <summary>
        /// Try to send a message, retrying if necessary.
        /// </summary
        private async Task<bool> TrySendMessage(Message message, string description, int maxAttempts = MaxSendAttempts)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (await this.device.SendMessage(message))
                {
                    return true;
                }

                logger.AddDebugMessage("Unable to send " + description + " message. Attempt #" + attempt.ToString());
            }

            return false;
        }

        /// <summary>
        /// Wait for an incoming message.
        /// </summary>
        private async Task<Message?> ReceiveMessage(CancellationToken cancellationToken)
        {
            Message? response = null;

            for (int pause = 0; pause < 3; pause++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                response = await this.device.ReceiveMessage();
                if (response == null)
                {
                    logger.AddDebugMessage("No response to read request yet.");
                    await Task.Delay(10);
                    continue;
                }

                break;
            }

            return response;
        }

        /// <summary>
        /// Read messages from the device, ignoring irrelevant messages.
        /// </summary>
        private async Task<bool> WaitForSuccess(Func<Message, Response<bool>> filter, CancellationToken cancellationToken, int attempts = MaxReceiveAttempts, Message? request = null)
        {
            // When the caller passes the request it just sent (e.g. a flash-write block), filter
            // inbound traffic to replies from that module for the duration of this wait. Callers
            // that don't (legacy) get the previous unfiltered behavior.
            IDisposable? filterScope = request != null
                ? this.device.FilterInbound(MessageFilters.RepliesFrom(request))
                : null;
            using (filterScope)
            {
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    Message message = await this.device.ReceiveMessage();
                    if (message == null)
                    {
                        await this.SendToolPresentNotification();
                        continue;
                    }

                    Response<bool> response = filter(message);
                    if ((response.Status != ResponseStatus.Success) && (response.Status != ResponseStatus.Refused))
                    {
                        logger.AddDebugMessage("Ignoring message: " + response.Status + "  " + message.ToString());
                        continue;
                    }

                    logger.AddDebugMessage("Found response, " + response.Status);
                    return response.Value;
                }
            }

            return false;
        }

        /// <summary>
        /// Send and receive a read-memory request.
        /// </summary>
        public async Task<Response<byte[]>> ReadMemory(
            Func<Message> messageFactory,
            Func<Message, Response<byte[]>> messageParser,
            CancellationToken cancellationToken)
        {
            Message message = messageFactory();

            if (!await this.device.SendMessage(message))
            {
                logger.AddDebugMessage("Unable to send read request.");
                return Response.Create<byte[]>(ResponseStatus.Error, new byte[0]);
            }

            ResponseStatus lastStatus = ResponseStatus.Error;
            // Filter the read payload to replies from the PCM we addressed. This is the bulk,
            // highest-volume loop, so on an in-car (noisy) bus it benefits most from dropping
            // off-conversation traffic before it eats into the receive budget.
            using (this.device.FilterInbound(MessageFilters.RepliesFrom(message)))
            {
                for (int receiveAttempt = 1; receiveAttempt <= MaxReceiveAttempts; receiveAttempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create<byte[]>(ResponseStatus.Cancelled, new byte[0]);
                    }

                    Message payloadMessage = await this.device.ReceiveMessage();
                    if (payloadMessage == null)
                    {
                        logger.AddDebugMessage("No payload following read request.");
                        continue;
                    }

                    logger.AddDebugMessage("Processing message");

                    Response<byte[]> payloadResponse = messageParser(payloadMessage);
                    if (payloadResponse.Status == ResponseStatus.Success)
                    {
                        return payloadResponse;
                    }

                    lastStatus = payloadResponse.Status;
                    logger.AddDebugMessage("Unable to process response: " + lastStatus + " " + payloadMessage.ToString());
                }
            }

            return Response.Create<byte[]>(lastStatus, new byte[0]);
        }
       
        public async Task<Response<int>> BeginCrankRelearn()
        {
            Message request = this.protocol.CreateCrankRelearnRequest();
            if (!await this.TrySendMessage(request, "Crank relearn request"))
            {
                logger.AddDebugMessage("Unable to send crank relearn request.");
                return Response.Create(ResponseStatus.Error, 0);
            }

            return Response.Create(ResponseStatus.Success, 1);
        }
    }
}

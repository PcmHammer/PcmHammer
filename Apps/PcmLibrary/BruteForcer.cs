// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The outcome of a brute-force operation.
    /// </summary>
    public enum BruteForceOutcome
    {
        /// <summary>A key (and, for the algorithm search, an algorithm) that unlocks the PCM was found.</summary>
        Found,

        /// <summary>Every candidate in the search space was tried without success.</summary>
        Exhausted,

        /// <summary>The PCM reported that it was already unlocked.</summary>
        AlreadyUnlocked,

        /// <summary>The PCM returned a seed of 0x0000, which means no unlock is required.</summary>
        UnlockNotRequired,

        /// <summary>The user cancelled the operation.</summary>
        Canceled,

        /// <summary>Communication with the PCM failed badly enough that we gave up.</summary>
        CommunicationError,
    }

    /// <summary>
    /// The result of a brute-force operation.
    /// </summary>
    public class BruteForceResult
    {
        public BruteForceOutcome Outcome { get; }

        /// <summary>The seed the PCM was using when the key was found (or last seen).</summary>
        public UInt16 Seed { get; }

        /// <summary>The key that unlocked the PCM (valid only when Outcome == Found).</summary>
        public UInt16 Key { get; }

        /// <summary>The algorithm number that produced the key, or -1 when not applicable.</summary>
        public int Algorithm { get; }

        public BruteForceResult(BruteForceOutcome outcome, UInt16 seed = 0, UInt16 key = 0, int algorithm = -1)
        {
            this.Outcome = outcome;
            this.Seed = seed;
            this.Key = key;
            this.Algorithm = algorithm;
        }
    }

    /// <summary>
    /// The classification of a single security-access key attempt.
    /// </summary>
    public enum SecurityUnlockResult
    {
        Unlocked,         // 0x34 - Security Access Allowed
        InvalidKey,       // 0x35 - Invalid Key
        Denied,           // 0x33 - Security Access Denied
        TooManyAttempts,  // 0x36 - Exceed Number of Attempts
        TimeDelayActive,  // 0x37 - Required Time Delay Not Expired
        NoResponse,       // The PCM did not answer.
        Unexpected,       // The PCM answered with something we don't recognise.
    }

    /// <summary>
    /// The phase the brute forcer is currently in, for live status reporting.
    /// </summary>
    public enum BruteForcePhase
    {
        /// <summary>Trying a key computed from a known GM algorithm (the "algo sweep").</summary>
        Sweeping,

        /// <summary>Trying a raw numeric key value.</summary>
        Trying,
    }

    /// <summary>
    /// A live progress update from a brute-force run, suitable for driving a UI.
    /// </summary>
    public class BruteForceProgress
    {
        /// <summary>Whether we are sweeping known algorithms or trying raw numeric keys.</summary>
        public BruteForcePhase Phase { get; set; }

        /// <summary>The key about to be (or being) tried.</summary>
        public UInt16 Key { get; set; }

        /// <summary>During the sweep, the algorithm index; otherwise -1.</summary>
        public int Algorithm { get; set; }

        /// <summary>Completion fraction (0..1) for a progress bar.</summary>
        public double Fraction { get; set; }

        /// <summary>A formatted estimate of the time remaining, or empty.</summary>
        public string Eta { get; set; } = string.Empty;

        /// <summary>
        /// False until the adaptive timing has locked in a security delay. While false the wait is
        /// still being stepped down, so the estimate is provisional; the UI shows a "calibrating"
        /// placeholder instead of <see cref="Eta"/>.
        /// </summary>
        public bool TimingCalibrated { get; set; }

        /// <summary>
        /// When greater than zero, the brute forcer is about to pause for roughly this many seconds
        /// (the security-access delay). The UI can drive a countdown from this; the other fields stay
        /// as they were for the attempt that triggered the wait. Zero on a normal attempt update.
        /// </summary>
        public double WaitSeconds { get; set; }
    }

    /// <summary>
    /// The result of requesting a security-access seed.
    /// </summary>
    public struct BruteForceSeedResult
    {
        public bool Success;
        public bool AlreadyUnlocked;

        /// <summary>
        /// The PCM refused to issue a seed because a security time-delay lockout is still active
        /// (it answered the seed request with status 0x37). The lockout has NOT cleared and the PCM
        /// is NOT unlocked - the caller should keep waiting.
        /// </summary>
        public bool DelayActive;
        public UInt16 Seed;

        public static BruteForceSeedResult Failure()
        {
            return new BruteForceSeedResult { Success = false, AlreadyUnlocked = false, Seed = 0 };
        }

        public static BruteForceSeedResult Unlocked()
        {
            return new BruteForceSeedResult { Success = true, AlreadyUnlocked = true, Seed = 0 };
        }

        public static BruteForceSeedResult Locked()
        {
            return new BruteForceSeedResult { Success = false, AlreadyUnlocked = false, DelayActive = true, Seed = 0 };
        }

        public static BruteForceSeedResult Ok(UInt16 seed)
        {
            return new BruteForceSeedResult { Success = true, AlreadyUnlocked = false, Seed = seed };
        }
    }

    /// <summary>
    /// Brute-forces the PCM's security access. This is the VPW equivalent of the "Brute Force
    /// Unlock" and "Brute Force Algo" tools in the CAN-bus E38 logger tool: it drives the existing
    /// PcmHammer security-access protocol in a loop instead of using a single configured key or
    /// algorithm.
    ///
    /// "Brute Force Unlock" tries every possible 16-bit key value against the PCM's seed.
    /// "Brute Force Algorithm" tries each of the 256 GM key algorithms, computing the matching key
    /// for the live seed via <see cref="KeyAlgorithm.GetKey"/>, until the PCM accepts one.
    ///
    /// GM PCMs enforce a security-access time delay between failed key attempts (10s by the GM spec,
    /// though some units are shorter). We deliberately do NOT probe the lockout to measure it:
    /// probing resets the PCM's delay timer and roughly doubles the effective wait. Instead we wait,
    /// make one attempt, and adapt the wait by stepping it down until the PCM pushes back. See
    /// <see cref="BruteForce"/>.
    /// </summary>
    public class BruteForcer
    {
        /// <summary>The number of GM key algorithms (indices 0x00..0xFF).</summary>
        public const int AlgorithmCount = 256;

        private readonly Vehicle vehicle;
        private readonly ILogger logger;
        private readonly IProgress<BruteForceProgress>? progress;

        // How many consecutive non-answers before we give up on the PCM.
        private const int MaxConsecutiveNoResponse = 20;

        // The security-access delay (whole seconds) between attempts, plus a small margin. The PCM
        // forces a time delay between key attempts and releases its seed (cheaply, as a "delay active"
        // response that is NOT a failed key attempt) before it will evaluate another key. So the
        // fastest approach is a SHORT delay: we poll the seed and fire the key the moment the lockout
        // clears, settling at roughly the PCM's own delay per key. A longer value is measurably slower
        // - it just spaces the attempts farther apart - so a small default suits every PCM. The user
        // can still raise it.
        public const int DefaultSecurityDelaySeconds = 2;
        public const int MaxSecurityDelaySeconds = 12;
        private static readonly TimeSpan SecurityDelaySafetyMargin = TimeSpan.FromMilliseconds(500);

        // If a single candidate stays locked out for longer than this, warn that the PCM may be stuck
        // or need a longer delay. Time-based (not poll-count based) so it does not cry wolf for a PCM
        // with a naturally long delay just because a short poll interval produced many polls.
        private static readonly TimeSpan LockoutWarnAfter = TimeSpan.FromSeconds(30);

        public BruteForcer(Vehicle vehicle, ILogger logger, IProgress<BruteForceProgress>? progress = null)
        {
            this.vehicle = vehicle;
            this.logger = logger;
            this.progress = progress;
        }

        /// <summary>
        /// Search the PCM's security key. If <paramref name="algoSweepFirst"/> is set, every known
        /// GM key algorithm is tried against the live seed first; then the numeric range
        /// <paramref name="start"/>..<paramref name="end"/> is swept, skipping any value already
        /// tried during the algorithm sweep. The first key the PCM accepts wins.
        ///
        /// The PCM forces a time delay between key attempts. We pace one seed+key attempt per
        /// caller-supplied delay (<paramref name="securityDelaySeconds"/> plus a small safety margin).
        /// While the PCM is counting down, a seed request answers "delay active" (cheap, and not a
        /// failed key attempt), so a short delay simply polls the seed and fires the key the moment the
        /// lockout clears - settling at roughly the PCM's own delay per key. A longer delay only spaces
        /// the attempts farther apart and is slower; the PCM's delay, not ours, sets the floor.
        /// </summary>
        public async Task<BruteForceResult> BruteForce(int start, int end, bool algoSweepFirst, int securityDelaySeconds, CancellationToken cancellationToken)
        {
            start &= 0xFFFF;
            end &= 0xFFFF;
            if (end < start)
            {
                end = start;
            }

            CandidateCursor cursor = new CandidateCursor(start, end, algoSweepFirst);
            UInt16 lastSeed = 0;

            // Clamp to the offered range, then add the safety margin (e.g. 10s -> 10.5s).
            securityDelaySeconds = Math.Max(0, Math.Min(MaxSecurityDelaySeconds, securityDelaySeconds));
            TimeSpan securityDelay = TimeSpan.FromSeconds(securityDelaySeconds) + SecurityDelaySafetyMargin;

            this.logger.AddUserMessage(
                $"Brute force started. Range 0x{start:X4}-0x{end:X4}. Algo sweep {(algoSweepFirst ? "on" : "off")}. " +
                $"Security delay {securityDelay.TotalSeconds:F1}s.");

            int noResponseStreak = 0;
            DateTime? lockoutStart = null; // when the current candidate first hit a lockout, or null
            bool lockoutWarned = false;

            // Wait the delay before the first attempt too: the PCM may already be partway through a
            // lockout from an earlier session, and waiting first is always safe.
            DateTime nextAttemptTime = DateTime.UtcNow + securityDelay;

            // Carried so the last presented candidate can be reported again if needed.
            BruteForcePhase lastPhase = algoSweepFirst ? BruteForcePhase.Sweeping : BruteForcePhase.Trying;
            UInt16 lastKey = 0;
            int lastAlgorithm = -1;

            // Each candidate is presented to the user once - one log line and one countdown - even
            // though unlocking it takes several seed/key exchanges behind the scenes. keyPresented
            // stays true across those internal retries and is cleared when we move to the next
            // candidate. perKeyEstimate is the expected wall-clock time for one key, learned from the
            // previous key, and drives both the countdown and the ETA so they reflect whole keys.
            bool keyPresented = false;
            DateTime keyPresentedAt = DateTime.UtcNow;
            TimeSpan perKeyEstimate = TimeSpan.FromSeconds(10);

            // Track how long the current candidate has been locked out (the PCM counting down its
            // forced delay), and warn once if that runs unusually long - a sign the PCM is stuck or
            // wants a longer delay, rather than a normal countdown.
            void NoteLockout()
            {
                DateTime stamp = DateTime.UtcNow;
                if (lockoutStart == null)
                {
                    lockoutStart = stamp;
                }
                else if (!lockoutWarned && stamp - lockoutStart.Value > LockoutWarnAfter)
                {
                    this.logger.AddUserMessage(
                        $"The PCM has held a security lockout for over {LockoutWarnAfter.TotalSeconds:F0}s; " +
                        "it may be stuck or need a longer security delay.");
                    lockoutWarned = true;
                }
            }

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!cursor.TryAdvanceToValid())
                    {
                        this.logger.AddUserMessage("Key not found.");
                        return new BruteForceResult(BruteForceOutcome.Exhausted, lastSeed);
                    }

                    // Wait out the security delay before touching the PCM. Nothing else talks to the
                    // PCM during this wait - that is what keeps its delay timer from being re-armed.
                    // We deliberately do not report these internal waits; the per-key countdown
                    // (started when the candidate is first presented) already covers them.
                    DateTime now = DateTime.UtcNow;
                    if (now < nextAttemptTime)
                    {
                        await Task.Delay(nextAttemptTime - now, cancellationToken);
                    }

                    // One attempt: request a seed, then send one key. The security delay is paced from
                    // after the key response (DateTime.UtcNow at each branch below), because the PCM's
                    // delay timer starts when it evaluates the key - not when we began the attempt.
                    BruteForceSeedResult seedResult = await this.vehicle.RequestSeedForBruteForce(cancellationToken);
                    if (seedResult.AlreadyUnlocked)
                    {
                        this.logger.AddUserMessage("The PCM is already unlocked.");
                        return new BruteForceResult(BruteForceOutcome.AlreadyUnlocked, lastSeed);
                    }
                    if (seedResult.DelayActive)
                    {
                        // The PCM refused the seed because it is still counting down its forced delay.
                        // Wait the same delay again and retry the same candidate (no key was consumed).
                        noResponseStreak = 0;
                        NoteLockout();
                        nextAttemptTime = DateTime.UtcNow + securityDelay;
                        continue;
                    }
                    if (!seedResult.Success)
                    {
                        this.logger.AddDebugMessage("Brute force: no seed response; retrying.");
                        if (++noResponseStreak >= MaxConsecutiveNoResponse)
                        {
                            this.logger.AddUserMessage("Brute force stopped: the PCM stopped responding.");
                            return new BruteForceResult(BruteForceOutcome.CommunicationError, lastSeed);
                        }
                        nextAttemptTime = DateTime.UtcNow + securityDelay;
                        continue;
                    }
                    if (seedResult.Seed == 0x0000)
                    {
                        this.logger.AddUserMessage("The PCM returned a seed of 0x0000; no unlock is required.");
                        return new BruteForceResult(BruteForceOutcome.UnlockNotRequired, 0);
                    }

                    UInt16 seed = seedResult.Seed;
                    lastSeed = seed;
                    (UInt16 key, BruteForcePhase phase, int algorithm) = cursor.Compute(seed);

                    // Present this candidate once: log it and start a single countdown for the whole
                    // key. Subsequent seed/key exchanges for the same candidate reuse this and stay
                    // silent, so the user sees one line and one timer per key.
                    if (!keyPresented)
                    {
                        keyPresented = true;
                        keyPresentedAt = DateTime.UtcNow;
                        lastPhase = phase;
                        lastKey = key;
                        lastAlgorithm = algorithm;
                        this.logger.AddUserMessage((phase == BruteForcePhase.Sweeping ? "Sweeping " : "Trying ") + key.ToString("X4"));
                        this.Report(phase, key, algorithm, cursor.Done, cursor.Total, perKeyEstimate, perKeyEstimate.TotalSeconds);
                    }

                    SecurityUnlockResult attempt = await this.vehicle.SendKeyForBruteForce(key, cancellationToken);
                    switch (attempt)
                    {
                        case SecurityUnlockResult.Unlocked:
                            int foundAlgo = phase == BruteForcePhase.Sweeping ? algorithm : IdentifyAlgorithm(seed, key);
                            this.logger.AddUserMessage(foundAlgo >= 0
                                ? $"Key found! {key:X4} (match Algo {foundAlgo}). Seed 0x{seed:X4}."
                                : $"Key found! {key:X4}. Seed 0x{seed:X4}.");
                            return new BruteForceResult(BruteForceOutcome.Found, seed, key, foundAlgo);

                        case SecurityUnlockResult.InvalidKey:
                        case SecurityUnlockResult.Denied:
                        case SecurityUnlockResult.Unexpected:
                            // The PCM evaluated the key (and it was wrong), so the delay had elapsed.
                            // Learn how long this key actually took so the next key's countdown/ETA is
                            // about right, then move on - the next candidate will be presented afresh.
                            // The countdown runs from presenting one key until presenting the next, so
                            // the estimate must include BOTH the time to evaluate this key and the
                            // pacing delay we wait afterwards before the next key; without that trailing
                            // delay the bar finished early and sat idle. This scales with any delay.
                            noResponseStreak = 0;
                            lockoutStart = null;
                            lockoutWarned = false;
                            TimeSpan took = DateTime.UtcNow - keyPresentedAt;
                            if (took >= TimeSpan.FromSeconds(2) && took <= TimeSpan.FromSeconds(60))
                            {
                                perKeyEstimate = took + securityDelay;
                            }
                            keyPresented = false;
                            cursor.Advance(key);
                            nextAttemptTime = DateTime.UtcNow + securityDelay;
                            break;

                        case SecurityUnlockResult.TooManyAttempts:
                        case SecurityUnlockResult.TimeDelayActive:
                            // The PCM hit its attempt limit and is forcing a delay; the key was not
                            // evaluated. Keep the candidate and retry after another full delay - we do
                            // not poke it sooner, because a mid-lockout attempt only restarts the timer.
                            noResponseStreak = 0;
                            NoteLockout();
                            nextAttemptTime = DateTime.UtcNow + securityDelay;
                            break;

                        case SecurityUnlockResult.NoResponse:
                        default:
                            if (++noResponseStreak >= MaxConsecutiveNoResponse)
                            {
                                this.logger.AddUserMessage("Brute force stopped: the PCM stopped responding.");
                                return new BruteForceResult(BruteForceOutcome.CommunicationError, lastSeed);
                            }
                            this.logger.AddDebugMessage("Brute force: no response; retrying same candidate.");
                            nextAttemptTime = DateTime.UtcNow + securityDelay;
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.AddUserMessage("Brute force stopped.");
                return new BruteForceResult(BruteForceOutcome.Canceled, lastSeed);
            }
        }

        /// <summary>
        /// Find a GM algorithm that produces <paramref name="key"/> from <paramref name="seed"/>,
        /// or -1 if none does.
        /// </summary>
        private static int IdentifyAlgorithm(UInt16 seed, UInt16 key)
        {
            for (int algo = 0; algo < AlgorithmCount; algo++)
            {
                if (KeyAlgorithm.GetKey(algo, seed) == key)
                {
                    return algo;
                }
            }
            return -1;
        }

        private void Report(BruteForcePhase phase, UInt16 key, int algorithm, int done, int total, TimeSpan perKeyEstimate, double waitSeconds = 0)
        {
            if (this.progress == null)
            {
                return;
            }

            double fraction = total > 0 ? Math.Min(1.0, (double)done / total) : 0.0;

            // The estimate is the measured time for one key times the remaining candidates - whole keys,
            // not the internal sub-operations. waitSeconds is that same per-key time, so the UI shows a
            // single countdown sized to one key.
            int remaining = Math.Max(0, total - done);
            string eta = FormatEta(TimeSpan.FromSeconds(perKeyEstimate.TotalSeconds * remaining));

            this.progress.Report(new BruteForceProgress
            {
                Phase = phase,
                Key = key,
                Algorithm = algorithm,
                Fraction = fraction,
                Eta = eta,
                TimingCalibrated = true,
                WaitSeconds = waitSeconds,
            });
        }

        /// <summary>
        /// Long-form remaining-time text, e.g. "3 Days, 11 Hours, 12 Mins". Days and hours are
        /// dropped once they are zero (from the most-significant end), and a sub-minute estimate
        /// rounds up to "1 Min" so the readout never sits at a misleading "0 Mins".
        /// </summary>
        private static string FormatEta(TimeSpan eta)
        {
            int days = (int)eta.TotalDays;
            int hours = eta.Hours;
            int minutes = eta.Minutes;

            // Round a non-zero remainder up to a whole minute so we never display "0 Mins".
            if (days == 0 && hours == 0 && minutes == 0 && eta.TotalSeconds > 0)
            {
                minutes = 1;
            }

            var parts = new List<string>();
            if (days > 0)
            {
                parts.Add($"{days} {(days == 1 ? "Day" : "Days")}");
            }
            if (hours > 0 || days > 0)
            {
                parts.Add($"{hours} {(hours == 1 ? "Hour" : "Hours")}");
            }
            parts.Add($"{minutes} {(minutes == 1 ? "Min" : "Mins")}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Walks the search space: first the 256 algorithm indices (when enabled), then the numeric
        /// key range, skipping any numeric value already produced by an algorithm during the sweep.
        /// The current candidate is only consumed when <see cref="Advance"/> is called, so a candidate
        /// that hits a lockout can be retried unchanged.
        /// </summary>
        private sealed class CandidateCursor
        {
            private readonly int end;
            private readonly HashSet<int> triedKeys = new HashSet<int>();
            private int algoIndex;
            private int current;

            public bool Sweeping { get; private set; }
            public int Done { get; private set; }
            public int Total { get; }

            public CandidateCursor(int start, int end, bool sweepFirst)
            {
                this.end = end;
                this.current = start;
                this.Sweeping = sweepFirst;
                this.Total = (sweepFirst ? AlgorithmCount : 0) + (end - start + 1);
            }

            /// <summary>
            /// Move to the next candidate that still needs trying, transitioning from the algorithm
            /// sweep to the numeric range and skipping already-tried values. Returns false when the
            /// search space is exhausted.
            /// </summary>
            public bool TryAdvanceToValid()
            {
                if (this.Sweeping && this.algoIndex >= AlgorithmCount)
                {
                    this.Sweeping = false;
                }
                if (!this.Sweeping)
                {
                    while (this.current <= this.end && this.triedKeys.Contains(this.current))
                    {
                        this.current++;
                        this.Done++;
                    }
                    if (this.current > this.end)
                    {
                        return false;
                    }
                }
                return true;
            }

            /// <summary>Compute the key for the current candidate against the given seed.</summary>
            public (UInt16 key, BruteForcePhase phase, int algorithm) Compute(UInt16 seed)
            {
                if (this.Sweeping)
                {
                    return (KeyAlgorithm.GetKey(this.algoIndex, seed), BruteForcePhase.Sweeping, this.algoIndex);
                }
                return ((UInt16)this.current, BruteForcePhase.Trying, -1);
            }

            /// <summary>Record the just-tried candidate (so the numeric phase can skip it) and advance.</summary>
            public void Advance(UInt16 triedKey)
            {
                if (this.Sweeping)
                {
                    this.triedKeys.Add(triedKey);
                    this.algoIndex++;
                }
                else
                {
                    this.current++;
                }
                this.Done++;
            }
        }
    }

    /// <summary>
    /// Security-access primitives used by the brute forcer. These deliberately perform a single
    /// seed request / single key attempt and report the raw outcome, so the BruteForcer can drive
    /// the loop and its own timing. They reuse the same Protocol building blocks as UnlockEcu.
    /// </summary>
    public partial class Vehicle
    {
        /// <summary>
        /// Request a single security-access seed.
        /// </summary>
        public async Task<BruteForceSeedResult> RequestSeedForBruteForce(CancellationToken cancellationToken)
        {
            await this.SetDeviceTimeout(TimeoutScenario.ReadProperty);
            this.device.ClearMessageQueue();

            Message seedRequest = this.protocol.CreateSeedRequest();
            if (!await this.TrySendMessage(seedRequest, "seed request"))
            {
                return BruteForceSeedResult.Failure();
            }

            for (int attempt = 1; attempt < MaxReceiveAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return BruteForceSeedResult.Failure();
                }

                Message seedResponse = await this.device.ReceiveMessage();
                if (seedResponse == null)
                {
                    continue;
                }

                byte[] bytes = seedResponse.GetBytes();

                // A seed request issued during a lockout comes back as 67 01 37 ("time delay not
                // expired"). That is the SAME byte pattern the legacy IsUnlocked() reads as
                // "already unlocked" - which is wrong mid-lockout, the very situation a brute-force
                // run is in. Check for the lockout first so we keep waiting instead of falsely
                // declaring success. (A genuinely unlocked PCM returns seed 0x0000, handled below.)
                if (this.protocol.IsSecurityDelayActive(bytes))
                {
                    return BruteForceSeedResult.Locked();
                }

                Response<UInt16> parsed = this.protocol.ParseSeed(bytes);
                if (parsed.Status == ResponseStatus.Success)
                {
                    return BruteForceSeedResult.Ok(parsed.Value);
                }
            }

            return BruteForceSeedResult.Failure();
        }

        /// <summary>
        /// Send a single candidate key and classify the PCM's response.
        /// </summary>
        public async Task<SecurityUnlockResult> SendKeyForBruteForce(UInt16 key, CancellationToken cancellationToken)
        {
            Message unlockRequest = this.protocol.CreateUnlockRequest(key);
            if (!await this.TrySendMessage(unlockRequest, "unlock request"))
            {
                return SecurityUnlockResult.NoResponse;
            }

            for (int attempt = 1; attempt < MaxReceiveAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return SecurityUnlockResult.NoResponse;
                }

                Message unlockResponse = await this.device.ReceiveMessage();
                if (unlockResponse == null)
                {
                    continue;
                }

                byte[] bytes = unlockResponse.GetBytes();
                if (bytes.Length >= 6 &&
                    bytes[3] == (Mode.Seed + Mode.Response) &&
                    bytes[4] == Submode.SendKey)
                {
                    switch (bytes[5])
                    {
                        case 0x34: return SecurityUnlockResult.Unlocked;        // Security Access Allowed
                        case 0x35: return SecurityUnlockResult.InvalidKey;      // Invalid Key
                        case 0x33: return SecurityUnlockResult.Denied;          // Security Access Denied
                        case 0x36: return SecurityUnlockResult.TooManyAttempts; // Exceed Number of Attempts
                        case 0x37: return SecurityUnlockResult.TimeDelayActive; // Required Time Delay Not Expired
                        default: return SecurityUnlockResult.Unexpected;
                    }
                }
            }

            return SecurityUnlockResult.NoResponse;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// False until the adaptive timing model has settled on a baseline. While false there is no
        /// meaningful time estimate yet (the fast calibration probes would badly under-estimate it),
        /// so the UI should show a "calibrating" placeholder instead of <see cref="Eta"/>.
        /// </summary>
        public bool TimingCalibrated { get; set; }

        /// <summary>
        /// When greater than zero, the brute forcer is about to pause for roughly this many seconds
        /// (a security-access lockout). The UI can drive a countdown from this; the other fields stay
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
    /// GM PCMs rate-limit security access: after a couple of bad keys they refuse further attempts
    /// for a few seconds (response codes 0x36 / 0x37). To avoid ever tripping a hard lockout we
    /// pace one attempt per cycle and back off when the PCM reports a lockout, mirroring the E38
    /// tool's timing model.
    /// </summary>
    public class BruteForcer
    {
        /// <summary>The number of GM key algorithms (indices 0x00..0xFF).</summary>
        public const int AlgorithmCount = 256;

        private readonly Vehicle vehicle;
        private readonly ILogger logger;
        private readonly IProgress<BruteForceProgress>? progress;

        // The ETA is measured only from steady-state attempts. The fast calibration probes (and the
        // very first lockout) would otherwise average into elapsed/done and make the estimate lurch
        // from "minutes" to "days" the moment a real lockout lands. These capture the elapsed time and
        // step count at the instant the timing model settled, so the estimate is built from the
        // steady run alone.
        private bool timingCalibrated;
        private TimeSpan calibratedAtElapsed;
        private int calibratedAtDone;

        // Adaptive pacing. Rather than assume a fixed period, we learn the PCM's real security-access
        // timing at run time. These constants bound and seed that process:
        //   - MinAttemptInterval: hard ceiling on attempt rate (10/sec), so even a PCM with no forced
        //     delay is not flooded; it also paces bursts within a window.
        //   - DelayProbeInterval: how often we re-poke the PCM while measuring a lockout delay.
        //   - DefaultLockoutDelay: fallback wait when we cannot measure the real delay.
        //   - MaxDelayMeasurement: give up measuring (and use the fallback) after this long.
        //   - DelaySafetyMargin: padding added to a measured delay so we never knock while still locked.
        private static readonly TimeSpan MinAttemptInterval = TimeSpan.FromMilliseconds(100); // 10 attempts/sec
        private static readonly TimeSpan DelayProbeInterval = TimeSpan.FromSeconds(1.0);
        private static readonly TimeSpan DefaultLockoutDelay = TimeSpan.FromSeconds(10.5);
        private static readonly TimeSpan MaxDelayMeasurement = TimeSpan.FromSeconds(20.0);
        private static readonly TimeSpan DelaySafetyMargin = TimeSpan.FromSeconds(0.75);
        private const int CalibrationProbeLimit = 12;
        private const int MaxConsecutiveNoResponse = 20;

        /// <summary>
        /// How the brute forcer currently believes the PCM gates security access, learned at run time.
        /// </summary>
        private enum TimingMode
        {
            /// <summary>Firing real keys back-to-back to learn how many are allowed per window (K).</summary>
            Calibrating,

            /// <summary>Hit a lockout; timing how long until the PCM evaluates a key again (T).</summary>
            MeasuringDelay,

            /// <summary>The PCM never locked out during calibration: run at the rate cap on one seed.</summary>
            SteadyNoLimit,

            /// <summary>Requesting a fresh seed clears the lockout: reseed every attempt.</summary>
            SteadyModelA,

            /// <summary>Fixed K attempts then a forced delay T: burst K keys, wait T, repeat.</summary>
            SteadyModelB,
        }

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
        /// Timing is learned at run time rather than fixed: we fire candidates as fast as the rate
        /// cap allows to discover how many keys the PCM evaluates before it locks out (K) and how
        /// long the lockout lasts (T), then settle into the fastest cadence that stays within those
        /// limits. See <see cref="TimingMode"/>.
        /// </summary>
        public async Task<BruteForceResult> BruteForce(int start, int end, bool algoSweepFirst, CancellationToken cancellationToken)
        {
            start &= 0xFFFF;
            end &= 0xFFFF;
            if (end < start)
            {
                end = start;
            }

            CandidateCursor cursor = new CandidateCursor(start, end, algoSweepFirst);
            UInt16 lastSeed = 0;

            this.logger.AddUserMessage(
                $"Brute force started. Range 0x{start:X4}-0x{end:X4}. Algo sweep {(algoSweepFirst ? "on" : "off")}.");
            this.logger.AddUserMessage("Calibrating PCM security timing...");

            // Learned-timing state.
            TimingMode mode = TimingMode.Calibrating;
            int evaluatedSinceReset = 0;                  // keys the PCM has evaluated since the last reset
            int attemptsPerWindow = 0;                    // K: attempts allowed before a forced delay
            int attemptsThisWindow = 0;                   // model B: evaluated keys in the in-progress window
            TimeSpan measuredDelay = DefaultLockoutDelay; // T
            DateTime lockoutStart = DateTime.UtcNow;
            bool firstProbe = false;                      // first measurement probe tests "does reseed reset?"
            int noResponseStreak = 0;

            // Cached seed. GM seeds are static within a session, so we avoid re-requesting it on every
            // attempt; we refresh it only when the timing model calls for it or after a link glitch.
            UInt16 cachedSeed = 0;
            bool haveSeed = false;

            Stopwatch operationTimer = Stopwatch.StartNew();
            DateTime nextAttemptTime = DateTime.UtcNow;
            DateTime lastAttemptLog = DateTime.MinValue;

            // Carried so a pacing wait can be reported (with a countdown) before the next candidate
            // is computed; the wait belongs to the attempt that just ran.
            BruteForcePhase lastReportedPhase = algoSweepFirst ? BruteForcePhase.Sweeping : BruteForcePhase.Trying;
            UInt16 lastReportedKey = 0;
            int lastReportedAlgorithm = -1;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Find the next untried candidate (and detect exhaustion).
                    if (!cursor.TryAdvanceToValid())
                    {
                        this.logger.AddUserMessage("Key not found.");
                        return new BruteForceResult(BruteForceOutcome.Exhausted, lastSeed);
                    }

                    // Pace this attempt.
                    DateTime now = DateTime.UtcNow;
                    if (now < nextAttemptTime)
                    {
                        TimeSpan wait = nextAttemptTime - now;

                        // A long pause is a security lockout. Report it here - right as the sleep
                        // starts and with the REAL remaining time - so the UI countdown matches the
                        // actual wait. (Reporting it earlier, before the seed I/O that can take several
                        // seconds, made the bar drain against a stale duration and finish early.)
                        if (wait >= TimeSpan.FromSeconds(2))
                        {
                            this.Report(lastReportedPhase, lastReportedKey, lastReportedAlgorithm, cursor.Done, cursor.Total, operationTimer, mode, wait.TotalSeconds);
                        }

                        await Task.Delay(wait, cancellationToken);
                    }
                    DateTime attemptStart = DateTime.UtcNow;

                    // Seed policy depends on the timing mode: reseed every attempt in model A, once per
                    // window in model B, once when first measuring (to test model A), else reuse cache.
                    bool freshSeed =
                        (mode == TimingMode.SteadyModelA) ||
                        (mode == TimingMode.SteadyModelB && attemptsThisWindow == 0) ||
                        (mode == TimingMode.MeasuringDelay && firstProbe) ||
                        !haveSeed;

                    if (freshSeed)
                    {
                        BruteForceSeedResult seedResult = await this.vehicle.RequestSeedForBruteForce(cancellationToken);
                        if (seedResult.AlreadyUnlocked)
                        {
                            this.logger.AddUserMessage("The PCM is already unlocked.");
                            return new BruteForceResult(BruteForceOutcome.AlreadyUnlocked, lastSeed);
                        }
                        if (seedResult.DelayActive)
                        {
                            // The PCM refuses to issue a seed while its security time delay is still
                            // running: the lockout has not cleared. A fresh seed therefore does NOT
                            // clear the lockout, so we are not in model A - measure the real delay by
                            // re-probing the seed until the PCM serves one again.
                            noResponseStreak = 0;
                            haveSeed = false;

                            if (mode != TimingMode.MeasuringDelay)
                            {
                                attemptsPerWindow = Math.Max(1, attemptsThisWindow > 0 ? attemptsThisWindow : evaluatedSinceReset);
                                this.logger.AddDebugMessage(
                                    $"Brute force: seed request reports the lockout is still active; measuring delay ({attemptsPerWindow} key(s)/window).");
                                mode = TimingMode.MeasuringDelay;
                                lockoutStart = attemptStart;
                            }
                            firstProbe = false; // a fresh seed was refused, so model A is already disproven

                            if (attemptStart - lockoutStart > MaxDelayMeasurement)
                            {
                                measuredDelay = DefaultLockoutDelay;
                                mode = TimingMode.SteadyModelB;
                                attemptsThisWindow = 0;
                                evaluatedSinceReset = 0;
                                this.logger.AddUserMessage(
                                    $"Calibrated: {attemptsPerWindow} keys/window; delay exceeded {MaxDelayMeasurement.TotalSeconds:F0}s, using {measuredDelay.TotalSeconds:F1}s fallback.");
                                nextAttemptTime = attemptStart + measuredDelay + DelaySafetyMargin;
                            }
                            else
                            {
                                nextAttemptTime = attemptStart + DelayProbeInterval; // keep polling the seed
                            }
                            continue;
                        }
                        if (!seedResult.Success)
                        {
                            this.logger.AddDebugMessage("Brute force: no seed response; retrying.");
                            haveSeed = false;
                            nextAttemptTime = attemptStart + MinAttemptInterval;
                            continue;
                        }
                        if (seedResult.Seed == 0x0000)
                        {
                            this.logger.AddUserMessage("The PCM returned a seed of 0x0000; no unlock is required.");
                            return new BruteForceResult(BruteForceOutcome.UnlockNotRequired, 0);
                        }
                        cachedSeed = seedResult.Seed;
                        haveSeed = true;
                    }

                    UInt16 seed = cachedSeed;
                    lastSeed = seed;

                    (UInt16 key, BruteForcePhase phase, int algorithm) = cursor.Compute(seed);

                    this.Report(phase, key, algorithm, cursor.Done, cursor.Total, operationTimer, mode);
                    lastReportedPhase = phase;
                    lastReportedKey = key;
                    lastReportedAlgorithm = algorithm;
                    this.logger.AddDebugMessage(phase == BruteForcePhase.Sweeping
                        ? $"Brute force: algo {algorithm}, seed 0x{seed:X4}, key 0x{key:X4}."
                        : $"Brute force: seed 0x{seed:X4}, trying key 0x{key:X4}.");
                    // Throttle the user-log line: at up to 10 attempts/sec the per-attempt detail would
                    // flood the results log, so cap it to ~1/sec there. The dialog status (via Report)
                    // and the debug log still update on every attempt.
                    if (attemptStart - lastAttemptLog >= TimeSpan.FromSeconds(1))
                    {
                        this.logger.AddUserMessage((phase == BruteForcePhase.Sweeping ? "Sweeping " : "Trying ") + key.ToString("X4"));
                        lastAttemptLog = attemptStart;
                    }

                    SecurityUnlockResult attempt = await this.vehicle.SendKeyForBruteForce(key, cancellationToken);

                    // Default cadence; specific outcomes override below.
                    TimeSpan delayAfter = MinAttemptInterval;

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
                            // The PCM evaluated the key (and it was wrong). We are inside the attempt
                            // window with no delay active, so the next candidate can go immediately.
                            noResponseStreak = 0;
                            evaluatedSinceReset++;
                            attemptsThisWindow++;

                            if (mode == TimingMode.MeasuringDelay)
                            {
                                // A probe was finally evaluated: the lockout has ended.
                                this.ConcludeDelayMeasurement(ref mode, ref measuredDelay, ref attemptsThisWindow,
                                    ref evaluatedSinceReset, firstProbe, attemptStart - lockoutStart, attemptsPerWindow);
                            }
                            else if (mode == TimingMode.Calibrating && evaluatedSinceReset >= CalibrationProbeLimit)
                            {
                                mode = TimingMode.SteadyNoLimit;
                                this.logger.AddUserMessage(
                                    $"Calibrated: no lockout after {evaluatedSinceReset} keys; running at up to 10/sec.");
                            }

                            cursor.Advance(key);

                            if (mode == TimingMode.SteadyModelB && attemptsThisWindow >= attemptsPerWindow)
                            {
                                // Window complete: wait out the forced delay before the next burst.
                                attemptsThisWindow = 0;
                                delayAfter = measuredDelay + DelaySafetyMargin;
                            }
                            else
                            {
                                delayAfter = MinAttemptInterval;
                            }
                            break;

                        case SecurityUnlockResult.TooManyAttempts:
                            // 0x36: the PCM started a forced delay and did NOT evaluate this key, so we
                            // keep the candidate and retry it once the delay clears.
                            noResponseStreak = 0;
                            if (mode == TimingMode.Calibrating || mode == TimingMode.SteadyNoLimit)
                            {
                                attemptsPerWindow = Math.Max(1, evaluatedSinceReset);
                                this.logger.AddDebugMessage($"Brute force: lockout after {attemptsPerWindow} keys; measuring delay.");
                                mode = TimingMode.MeasuringDelay;
                                lockoutStart = attemptStart;
                                firstProbe = true;
                                delayAfter = MinAttemptInterval; // probe soon; the probe reseeds to test model A
                            }
                            else if (mode == TimingMode.SteadyModelB)
                            {
                                // Locked out earlier than expected: tighten K and start a fresh wait.
                                attemptsPerWindow = Math.Max(1, attemptsThisWindow);
                                attemptsThisWindow = 0;
                                delayAfter = measuredDelay + DelaySafetyMargin;
                            }
                            else
                            {
                                // We are in SteadyModelA, which assumes a fresh seed clears the lockout -
                                // yet this reseeded attempt still tripped it. In true model A the counter
                                // resets every attempt and 0x36 can never occur, so a single one disproves
                                // the model (the lone calibration probe was a false positive). Demote:
                                // measure the real time delay and settle into model B.
                                attemptsPerWindow = Math.Max(1, attemptsThisWindow);
                                this.logger.AddUserMessage(
                                    $"A new seed did not clear the lockout after all; measuring the real delay ({attemptsPerWindow} key(s)/window).");
                                mode = TimingMode.MeasuringDelay;
                                lockoutStart = attemptStart;
                                firstProbe = false; // reseed is already disproven; measure the time delay directly
                                delayAfter = DelayProbeInterval;
                            }
                            break;

                        case SecurityUnlockResult.TimeDelayActive:
                            // 0x37: still locked out; key not evaluated. Keep the candidate.
                            noResponseStreak = 0;
                            if (mode == TimingMode.MeasuringDelay)
                            {
                                firstProbe = false;
                                if (attemptStart - lockoutStart > MaxDelayMeasurement)
                                {
                                    measuredDelay = DefaultLockoutDelay;
                                    mode = TimingMode.SteadyModelB;
                                    attemptsThisWindow = 0;
                                    evaluatedSinceReset = 0;
                                    this.logger.AddUserMessage(
                                        $"Calibrated: {attemptsPerWindow} keys/window; delay exceeded {MaxDelayMeasurement.TotalSeconds:F0}s, using {measuredDelay.TotalSeconds:F1}s fallback.");
                                    delayAfter = measuredDelay + DelaySafetyMargin;
                                }
                                else
                                {
                                    delayAfter = DelayProbeInterval; // keep probing the same key
                                }
                            }
                            else if (mode == TimingMode.SteadyModelA)
                            {
                                // 0x37 in model A also disproves "a fresh seed clears the lockout" (see the
                                // 0x36 handler). Measure the real delay and switch to model B.
                                attemptsPerWindow = Math.Max(1, attemptsThisWindow);
                                this.logger.AddUserMessage(
                                    $"A new seed did not clear the lockout after all; measuring the real delay ({attemptsPerWindow} key(s)/window).");
                                mode = TimingMode.MeasuringDelay;
                                lockoutStart = attemptStart;
                                firstProbe = false;
                                delayAfter = DelayProbeInterval;
                            }
                            else
                            {
                                // Mistimed in steady state: wait a full delay and restart the window.
                                attemptsThisWindow = 0;
                                delayAfter = (measuredDelay > TimeSpan.Zero ? measuredDelay : DefaultLockoutDelay) + DelaySafetyMargin;
                            }
                            break;

                        case SecurityUnlockResult.NoResponse:
                        default:
                            noResponseStreak++;
                            if (noResponseStreak >= MaxConsecutiveNoResponse)
                            {
                                this.logger.AddUserMessage("Brute force stopped: the PCM stopped responding.");
                                return new BruteForceResult(BruteForceOutcome.CommunicationError, lastSeed);
                            }
                            this.logger.AddDebugMessage("Brute force: no response; retrying same candidate.");
                            haveSeed = false; // re-establish the seed in case the link glitched
                            delayAfter = MinAttemptInterval;
                            break;
                    }

                    nextAttemptTime = attemptStart + (delayAfter > MinAttemptInterval ? delayAfter : MinAttemptInterval);
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.AddUserMessage("Brute force stopped.");
                return new BruteForceResult(BruteForceOutcome.Canceled, lastSeed);
            }
        }

        /// <summary>
        /// A measurement probe was just evaluated, so the lockout has ended. Decide whether requesting
        /// a fresh seed cleared it (model A) or a genuine time delay elapsed (model B), record the
        /// learned values, and open a fresh window for the attempt that just succeeded.
        /// </summary>
        private void ConcludeDelayMeasurement(ref TimingMode mode, ref TimeSpan measuredDelay,
            ref int attemptsThisWindow, ref int evaluatedSinceReset, bool firstProbe, TimeSpan elapsed, int attemptsPerWindow)
        {
            if (firstProbe)
            {
                // We reseeded immediately after the lockout and the key was evaluated at once:
                // requesting a seed clears the attempt counter (model A).
                mode = TimingMode.SteadyModelA;
                measuredDelay = TimeSpan.Zero;
                this.logger.AddUserMessage("Calibrated: a new seed clears the lockout; reseeding each attempt at up to 10/sec.");
            }
            else
            {
                mode = TimingMode.SteadyModelB;
                measuredDelay = elapsed;
                this.logger.AddUserMessage($"Calibrated: {attemptsPerWindow} keys/window, ~{elapsed.TotalSeconds:F1}s delay.");
            }

            // The attempt that just evaluated opens the new window.
            attemptsThisWindow = 1;
            evaluatedSinceReset = 1;
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

        private void Report(BruteForcePhase phase, UInt16 key, int algorithm, int done, int total, Stopwatch operationTimer, TimingMode mode, double waitSeconds = 0)
        {
            if (this.progress == null)
            {
                return;
            }

            double fraction = total > 0 ? Math.Min(1.0, (double)done / total) : 0.0;

            // Capture the baseline the first time the timing model settles into a steady mode, so the
            // estimate ignores the fast calibration probes that came before it.
            bool steady = mode == TimingMode.SteadyNoLimit
                || mode == TimingMode.SteadyModelA
                || mode == TimingMode.SteadyModelB;
            if (steady && !this.timingCalibrated)
            {
                this.timingCalibrated = true;
                this.calibratedAtElapsed = operationTimer.Elapsed;
                this.calibratedAtDone = done;
            }

            // Build the estimate only from steady-state attempts. Until at least one of those has
            // completed there is no honest number to show, so leave Eta empty and flag it as not
            // yet calibrated; the UI shows a "calibrating" placeholder instead.
            string eta = string.Empty;
            bool calibrated = false;
            if (this.timingCalibrated)
            {
                int stepsSince = done - this.calibratedAtDone;
                if (stepsSince > 0)
                {
                    double secondsPerStep = (operationTimer.Elapsed - this.calibratedAtElapsed).TotalSeconds / stepsSince;
                    int remaining = Math.Max(0, total - done);
                    eta = FormatEta(TimeSpan.FromSeconds(secondsPerStep * remaining));
                    calibrated = true;
                }
            }

            this.progress.Report(new BruteForceProgress
            {
                Phase = phase,
                Key = key,
                Algorithm = algorithm,
                Fraction = fraction,
                Eta = eta,
                TimingCalibrated = calibrated,
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

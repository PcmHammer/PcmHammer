// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Drives a logging session and hands each row of readings back through events, so a front end
    /// only has to start it, subscribe, and display. The connect/open/poll/clean-up work lives here
    /// (and in <see cref="LogSession"/>) rather than in each UI, so every front end shares one
    /// implementation and the UI stays "dumb".
    ///
    /// Thread note: the loop runs on a background task, so <see cref="RowAvailable"/> and the other
    /// events fire off the UI thread. A front end must marshal them to its own thread (e.g. Avalonia
    /// Dispatcher.UIThread, WinForms Control.Invoke) before touching the UI.
    ///
    /// Handles the live-reading loop for a fixed set of columns and optional CSV recording. Profile
    /// hot-reconfiguration, the auxiliary bus and bus hand-over are intentionally left for later
    /// milestones; the WinForms PcmLogger loop (MainForm.LoggingThread.cs) is the reference for those.
    /// </summary>
    public sealed class LoggingController
    {
        private readonly Vehicle vehicle;
        private readonly uint osid;
        private readonly ILogger logger;

        private readonly object sync = new object();
        private CancellationTokenSource? cancellation;
        private Task? loopTask;

        // Bus hand-over: a reader (trouble codes, etc.) can borrow the bus from the running loop. The
        // loop yields between rows - the session and any recording stay open - so logging resumes a few
        // rows short rather than being stopped. Mirrors the WinForms PcmLogger BorrowBus/ReturnBus.
        private volatile bool busHandoverRequested;
        private readonly ManualResetEventSlim busIdle = new ManualResetEventSlim(false);

        // Live reconfiguration: the UI can change the logged columns while running; the loop reopens
        // the session with the new set between rows (as the WinForms logger does on a profile change).
        private IReadOnlyList<LogColumn>? pendingColumns;
        private volatile bool reconfigureRequested;

        // CSV recording. The loop writes each row to the recorder while one is set; start/stop manage
        // the file. Guarded by recordLock so the loop never writes to a stream being closed.
        private readonly object recordLock = new object();
        private LogFileWriter? recorder;
        private StreamWriter? recordStream;
        private IReadOnlyList<string> columnNames = Array.Empty<string>();
        private int recordedRows;
        private string? recordPath;

        /// <summary>Raised once per complete row of readings, in column order.</summary>
        public event Action<IReadOnlyList<LogRowElement>>? RowAvailable;

        /// <summary>Raised once after Start with any parameters the PCM refused, so the UI can drop them.</summary>
        public event Action<IReadOnlyList<Parameter>>? UnsupportedParametersReported;

        /// <summary>Raised on the loop thread once the session is open and about to poll.</summary>
        public event Action? Started;

        /// <summary>Raised on the loop thread when the loop has ended and the session is released.</summary>
        public event Action? Stopped;

        /// <summary>Raised when the session could not start or the loop faulted.</summary>
        public event Action<Exception>? Failed;

        /// <summary>Raised when recording starts (with the file path) or stops (with the row count).</summary>
        public event Action<string>? RecordingStarted;
        public event Action<int>? RecordingStopped;

        /// <summary>Raised on the loop thread after the session reopens with a new column set (the new count).</summary>
        public event Action<int>? Reconfigured;

        public LoggingController(Vehicle vehicle, uint osid, ILogger logger)
        {
            this.vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            this.osid = osid;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>True while the polling loop is running.</summary>
        public bool IsRunning
        {
            get
            {
                lock (this.sync)
                {
                    return this.loopTask != null && !this.loopTask.IsCompleted;
                }
            }
        }

        /// <summary>
        /// Begin logging the given columns. Does nothing if already running. Returns immediately; the
        /// loop runs in the background and reports through the events.
        /// </summary>
        public void Start(IReadOnlyList<LogColumn> columns)
        {
            if (columns == null)
            {
                throw new ArgumentNullException(nameof(columns));
            }

            lock (this.sync)
            {
                if (this.loopTask != null && !this.loopTask.IsCompleted)
                {
                    return;
                }

                this.cancellation = new CancellationTokenSource();
                CancellationToken token = this.cancellation.Token;
                this.loopTask = Task.Run(() => this.RunLoop(columns, token));
            }
        }

        /// <summary>
        /// Stop logging and wait for the loop to release the bus, so the caller can safely use the
        /// connection (read, write, clear codes) immediately afterward.
        /// </summary>
        public async Task StopAsync()
        {
            Task? running;
            CancellationTokenSource? cts;
            lock (this.sync)
            {
                running = this.loopTask;
                cts = this.cancellation;
            }

            cts?.Cancel();

            if (running != null)
            {
                try
                {
                    await running.ConfigureAwait(false);
                }
                catch
                {
                    // The loop reports its own failures through Failed; nothing to do here.
                }
            }

            lock (this.sync)
            {
                if (ReferenceEquals(this.loopTask, running))
                {
                    this.loopTask = null;
                    this.cancellation?.Dispose();
                    this.cancellation = null;
                }
            }
        }

        /// <summary>
        /// Borrow the bus from the running loop so the caller can read or clear codes without stopping
        /// logging. The loop yields between rows (the session and any recording stay open), so logging
        /// resumes with only a few rows' gap rather than being torn down. Returns true once the loop has
        /// released the bus - or immediately if nothing is logging. Returns false if the loop did not
        /// yield within <paramref name="timeout"/> (mid-exchange), in which case the caller should leave
        /// it alone. Always call <see cref="ReturnBus"/> when done (a using/try-finally is simplest).
        /// </summary>
        public async Task<bool> BorrowBusAsync(TimeSpan timeout)
        {
            if (!this.IsRunning)
            {
                // Nothing is using the bus.
                return true;
            }

            this.busIdle.Reset();
            this.busHandoverRequested = true;

            // Off the caller's thread: a UI thread must not block here, and the loop marshals nothing.
            bool idle = await Task.Run(() => this.busIdle.Wait(timeout)).ConfigureAwait(false);

            if (!idle)
            {
                // Mid-exchange and not yielding. Leave the loop alone rather than talk over it.
                this.busHandoverRequested = false;
            }

            return idle;
        }

        /// <summary>Give the bus back to the logging loop after a <see cref="BorrowBusAsync"/>.</summary>
        public void ReturnBus()
        {
            this.busHandoverRequested = false;
        }

        /// <summary>
        /// Replace the logged columns while running, without stopping - the loop reopens the session
        /// with the new set between rows (like the WinForms logger on a profile change), so a parameter
        /// added to the view starts reading live. An active recording is closed first (its header would
        /// no longer match the new columns); start a new recording for the new set if wanted. Does
        /// nothing if not running - the caller should <see cref="Start"/> instead.
        /// </summary>
        public void Reconfigure(IReadOnlyList<LogColumn> columns)
        {
            if (columns == null)
            {
                throw new ArgumentNullException(nameof(columns));
            }

            lock (this.sync)
            {
                if (this.loopTask == null || this.loopTask.IsCompleted)
                {
                    return;
                }

                this.pendingColumns = columns;
                this.reconfigureRequested = true;
            }
        }

        /// <summary>True while a recording file is open.</summary>
        public bool IsRecording
        {
            get { lock (this.recordLock) { return this.recorder != null; } }
        }

        /// <summary>Rows written to the current (or last) recording.</summary>
        public int RecordedRowCount
        {
            get { lock (this.recordLock) { return this.recordedRows; } }
        }

        /// <summary>
        /// Begin writing each logged row to a CSV file at <paramref name="path"/>. Logging must already
        /// be running (so the columns are known). Stops any current recording first.
        /// </summary>
        public async Task StartRecordingAsync(string path)
        {
            IReadOnlyList<string> columns;
            lock (this.recordLock)
            {
                columns = this.columnNames;
            }

            if (columns.Count == 0)
            {
                throw new InvalidOperationException("Start logging before recording.");
            }

            this.StopRecording();

            StreamWriter stream = new StreamWriter(path, append: false);
            LogFileWriter writer = new LogFileWriter(stream, null);
            await writer.WriteHeader(columns).ConfigureAwait(false);

            lock (this.recordLock)
            {
                this.recordStream = stream;
                this.recorder = writer;
                this.recordedRows = 0;
                this.recordPath = path;
            }

            this.RecordingStarted?.Invoke(path);
        }

        /// <summary>Close the recording file, if one is open.</summary>
        public void StopRecording()
        {
            StreamWriter? stream;
            int rows;
            lock (this.recordLock)
            {
                if (this.recorder == null)
                {
                    return;
                }

                stream = this.recordStream;
                rows = this.recordedRows;
                this.recorder = null;
                this.recordStream = null;
            }

            try
            {
                stream?.Flush();
                stream?.Dispose();
            }
            catch
            {
                // Best effort: the file is being closed regardless.
            }

            this.RecordingStopped?.Invoke(rows);
        }

        /// <summary>Create, open and start a session for the given columns, publishing its headings and
        /// any unsupported parameters. Used for the first open and each live reconfiguration.</summary>
        private async Task<LogSession> OpenSessionAsync(IReadOnlyList<LogColumn> columns, CancellationToken token)
        {
            LogSession session = new LogSession(
                new IParameterSource[]
                {
                    new PcmParameterSource(this.vehicle, this.osid, this.logger),
                });

            if (!await session.Open(columns, token).ConfigureAwait(false))
            {
                session.Dispose();
                throw new LogStartFailedException();
            }

            await session.Start(token).ConfigureAwait(false);

            // Column headings for any recording started while this session runs.
            lock (this.recordLock)
            {
                this.columnNames = session.GetColumnNames().ToList();
            }

            if (session.UnsupportedParameters.Count > 0)
            {
                this.UnsupportedParametersReported?.Invoke(session.UnsupportedParameters);
            }

            return session;
        }

        private async Task RunLoop(IReadOnlyList<LogColumn> columns, CancellationToken token)
        {
            LogSession? session = null;
            try
            {
                session = await this.OpenSessionAsync(columns, token).ConfigureAwait(false);
                this.Started?.Invoke();

                while (!token.IsCancellationRequested)
                {
                    // A live reconfiguration (the logged columns changed) reopens the session between
                    // rows with the new set. The recording's header would no longer match the new
                    // columns, so it is closed first; the session stays live with the new columns.
                    if (this.reconfigureRequested)
                    {
                        IReadOnlyList<LogColumn> next;
                        lock (this.sync)
                        {
                            next = this.pendingColumns ?? columns;
                            this.reconfigureRequested = false;
                        }

                        this.StopRecording();

                        try
                        {
                            await session.Close().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Best effort: reopening regardless.
                        }

                        session.Dispose();

                        columns = next;
                        session = await this.OpenSessionAsync(columns, token).ConfigureAwait(false);
                        this.Reconfigured?.Invoke(columns.Count);
                        continue;
                    }

                    // Between rows is the only safe moment to hand the bus over: one iteration is at most
                    // one complete exchange, so nothing is left half-said. Suspend first so a streaming
                    // PCM stops sending (or the borrower would read log rows instead of its own answers),
                    // signal idle, wait for the borrower to return the bus, then resume. The session and
                    // any recording stay open, so logging picks up where it left off, a few rows short.
                    if (this.busHandoverRequested)
                    {
                        await session.Suspend().ConfigureAwait(false);
                        this.busIdle.Set();

                        while (this.busHandoverRequested && !token.IsCancellationRequested)
                        {
                            await Task.Delay(20, token).ConfigureAwait(false);
                        }

                        this.busIdle.Reset();

                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        await session.Resume().ConfigureAwait(false);
                        continue;
                    }

                    // An incomplete row comes back empty rather than null.
                    IReadOnlyList<LogRowElement> row = await session.Read(token).ConfigureAwait(false);
                    if (row.Count > 0)
                    {
                        this.RowAvailable?.Invoke(row);

                        lock (this.recordLock)
                        {
                            if (this.recorder != null)
                            {
                                this.recorder.WriteLine(row.Select(element => element.ValueAsString));
                                this.recordedRows++;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop.
            }
            catch (Exception exception)
            {
                this.logger.AddUserMessage("Logging stopped: " + exception.Message);
                this.logger.AddDebugMessage(exception.ToString());
                this.Failed?.Invoke(exception);
            }
            finally
            {
                // Close any open recording and forget the columns before the session goes away.
                this.StopRecording();
                lock (this.recordLock)
                {
                    this.columnNames = Array.Empty<string>();
                }

                if (session != null)
                {
                    try
                    {
                        await session.Close().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort: the bus is being released regardless.
                    }

                    session.Dispose();
                }

                this.Stopped?.Invoke();
            }
        }
    }
}

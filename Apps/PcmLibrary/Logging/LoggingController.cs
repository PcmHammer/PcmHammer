// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
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
    /// This first cut handles the live-reading loop for a fixed set of columns. CSV recording,
    /// profile hot-reconfiguration, the auxiliary bus and bus hand-over are intentionally left for
    /// later milestones; the WinForms PcmLogger loop (MainForm.LoggingThread.cs) is the reference
    /// for those.
    /// </summary>
    public sealed class LoggingController
    {
        private readonly Vehicle vehicle;
        private readonly uint osid;
        private readonly ILogger logger;

        private readonly object sync = new object();
        private CancellationTokenSource? cancellation;
        private Task? loopTask;

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

        private async Task RunLoop(IReadOnlyList<LogColumn> columns, CancellationToken token)
        {
            LogSession? session = null;
            try
            {
                session = new LogSession(
                    new IParameterSource[]
                    {
                        new PcmParameterSource(this.vehicle, this.osid, this.logger),
                    });

                if (!await session.Open(columns, token).ConfigureAwait(false))
                {
                    throw new LogStartFailedException();
                }

                await session.Start(token).ConfigureAwait(false);

                if (session.UnsupportedParameters.Count > 0)
                {
                    this.UnsupportedParametersReported?.Invoke(session.UnsupportedParameters);
                }

                this.Started?.Invoke();

                while (!token.IsCancellationRequested)
                {
                    // An incomplete row comes back empty rather than null.
                    IReadOnlyList<LogRowElement> row = await session.Read(token).ConfigureAwait(false);
                    if (row.Count > 0)
                    {
                        this.RowAvailable?.Invoke(row);
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

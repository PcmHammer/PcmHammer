// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    public class ZoomedParameter
    {
        public LogColumn LogColumn { get; private set; }
        public string Value { get; private set; }
        public string Name { get; private set; }
        public string Units { get; private set; }

        public ZoomedParameter(LogColumn logColumn, string value, string name, string units)
        {
            LogColumn = logColumn;
            Value = value;
            Name = name;
            Units = units;
        }
    }

    partial class MainForm
    {
        // Rows carry LogRowElement rather than bare strings: the dashboard needs the numeric value,
        // and the element still carries the formatted string the file and the text display use, so
        // nothing downstream changes.
        private ConcurrentQueue<Tuple<LogFileWriter?, IReadOnlyList<LogRowElement>>> logRowQueue = new ConcurrentQueue<Tuple<LogFileWriter?, IReadOnlyList<LogRowElement>>>();

        private AutoResetEvent endWriterThread = new AutoResetEvent(false);
        private AutoResetEvent rowAvailable = new AutoResetEvent(false);

        private static DateTime LastLogTime;

        private ManualResetEvent loggerThreadEnded = new ManualResetEvent(false);
        private ManualResetEvent writerThreadEnded = new ManualResetEvent(false);
        
        enum LogState
        {
            Nothing,
            DisplayOnly,
            StartSaving,
            Saving,
            StopSaving
        }

        private volatile LogState logState = LogState.Nothing;

        AuxiliaryBusLogger auxiliaryBusLogger = null!;

        /// <summary>
        /// The interface the auxiliary bus is reading through. Owned here rather than by a logging
        /// session because it remains open across profile changes and is reopened only when the
        /// auxiliary interface selection changes.
        /// </summary>
        private Device? auxiliaryDevice;

        private DeviceSelection? auxiliaryDeviceSelection;

        private IDisposable? auxiliaryDeviceClaim;

        /// <summary>The auxiliary interface being opened in the background, if one is.</summary>
        private Task? auxiliaryBusTask;

        /// <summary>
        /// Rows written to the log file since recording started. Incremented on the writer thread and
        /// read by the display timer, so it is only ever touched through Interlocked/Volatile.
        /// </summary>
        private int recordedFrames;

        /// <summary>Where the recording in progress is being written, for the save prompt.</summary>
        private string? recordedLogPath;

        /// <summary>Original start time and columns for appending after a disconnect/reconnect.</summary>
        private DateTime recordedStartTime;
        private string[]? recordedColumnNames;

        /// <summary>
        /// Whether the log file is still open. Set on the logging thread, read by the UI: Save moves
        /// the file, so it must not be offered until the writer has let go of it.
        /// </summary>
        private volatile bool recordingFileOpen;

        /// <summary>Set by the UI when it needs the bus for something else.</summary>
        private volatile bool busHandoverRequested;

        /// <summary>Signalled by the logging thread once it has stopped using the bus.</summary>
        private readonly ManualResetEventSlim busIdle = new ManualResetEventSlim(false);

        /// <summary>
        /// Stop logging long enough for another operation to use the bus, and hand it back after.
        /// </summary>
        /// <remarks>
        /// So that reading or clearing codes does not need the user to disconnect first. The cost is
        /// a few missing rows in the middle of a recording, which is not worth a trip through the
        /// connect and disconnect buttons to avoid.
        ///
        /// The wait is off the UI thread deliberately: the logging thread marshals its messages to
        /// the UI, so blocking here would leave the two of them waiting on each other.
        /// </remarks>
        private async Task<bool> BorrowBus()
        {
            if (!this.viewing)
            {
                // Nothing is using it.
                return true;
            }

            this.busHandoverRequested = true;

            bool idle = await Task.Run(() => this.busIdle.Wait(TimeSpan.FromSeconds(5)));

            if (!idle)
            {
                // Mid-exchange and not yielding. Leave the logger alone rather than talk over it.
                this.busHandoverRequested = false;
            }

            return idle;
        }

        private void ReturnBus()
        {
            this.busHandoverRequested = false;
        }

        /// <summary>
        /// Create a string that will look reasonable in the UI's main text box, and
        /// build a list of parameters to show in a larger format.
        /// 
        /// TODO: Instead of a string for a text box, make a list of objects, and render
        /// them as a grid.
        /// </summary>
        private Tuple<string, List<ZoomedParameter>> FormatValuesForDisplay(IReadOnlyList<LogRowElement> row)
        {
            List<ZoomedParameter> zoomedParameters = new List<ZoomedParameter>();
            StringBuilder builder = new StringBuilder();

            // The row carries each value's name and units, so this no longer has to walk the
            // logger's internals in step with it - which is what used to re-read the auxiliary
            // values after the row had already consumed them. Zoom is the one thing the row does
            // not carry, being a property of the profile's columns, so the two are matched by
            // parameter id. Captured once: the profile can be replaced while this runs.
            LogProfile? profile = this.currentProfile;
            Dictionary<string, LogColumn> zoomable = new Dictionary<string, LogColumn>();
            if (profile != null)
            {
                foreach (LogColumn column in profile.Columns)
                {
                    if (column.Zoom)
                    {
                        zoomable[column.Parameter.Id] = column;
                    }
                }
            }

            foreach (LogRowElement element in row)
            {
                builder.Append(element.ValueAsString);
                builder.Append('\t');
                builder.Append(element.Units);
                builder.Append('\t');
                builder.AppendLine(element.ParameterName);

                if (zoomable.TryGetValue(element.ParameterId, out LogColumn zoomColumn))
                {
                    zoomedParameters.Add(
                        new ZoomedParameter(
                            zoomColumn,
                            element.ValueAsString,
                            element.ParameterName,
                            element.Units));
                }
            }

            DateTime now = DateTime.Now;
            builder.AppendLine((now - LastLogTime).TotalMilliseconds.ToString("0.00") + "\tms\tQuery time");
            LastLogTime = now;

            return new Tuple<string, List<ZoomedParameter>>(builder.ToString(), zoomedParameters);
        }

        /// <summary>
        /// Everything a logging session is configured to do: what the profile asks the PCM for, and
        /// which interface serves the auxiliary bus. Used instead of comparing LogProfile instances,
        /// which change identity far more often than they change meaning.
        /// </summary>
        private string SessionSignature(LogProfile? profile)
        {
            StringBuilder builder = new StringBuilder();

            if (profile != null)
            {
                foreach (LogColumn column in profile.Columns)
                {
                    builder.Append(column.Parameter.Id)
                        .Append('|')
                        .Append(column.Conversion.Units)
                        .Append('|')
                        .Append(column.Zoom ? '1' : '0')
                        .Append(';');
                }
            }

            // Included so that changing the auxiliary interface - or turning it off after it failed
            // to open - rebuilds the session. Without it the only way to retry was to re-pick the
            // PCM interface, which releases the Vehicle and resets the loop.
            return builder
                .Append('#')
                .Append((this.AuxiliaryBusEnabled ? this.AuxiliarySelection : DeviceSelection.None).Describe())
                .ToString();
        }

        /// <summary>
        /// Open the auxiliary interface if one is wanted, and point the auxiliary logger at it.
        /// </summary>
        /// <remarks>
        /// The interface is reused across logger rebuilds and only reopened when the selection
        /// changes. A failure here is reported and then ignored: a second bus that will not open is
        /// no reason to abandon logging the PCM.
        ///
        /// Takes its logger explicitly so the selected source instance is clear while it is being
        /// configured for the current session.
        /// </remarks>
        private async Task StartAuxiliaryBus(AuxiliaryBusLogger busLogger)
        {
            DeviceSelection wanted = this.AuxiliaryBusEnabled ? this.AuxiliarySelection : DeviceSelection.None;
            bool sameSelection = wanted.Equals(this.auxiliaryDeviceSelection);

            // Profile changes do not change the broadcast source. Keep its native monitor channel
            // open instead of disconnecting and reconnecting the J2534 driver for every rebuild.
            if (sameSelection && (busLogger.IsRunning || !wanted.IsUsable))
            {
                return;
            }

            if (!sameSelection)
            {
                // Stop the read loop and end its monitor channel before disposing the device. The
                // vendor DLL must not still be servicing ReceiveCanFrame when it is closed.
                await busLogger.Stop();
                this.ReleaseAuxiliaryDevice();

                if (wanted.IsUsable)
                {
                    try
                    {
                        this.auxiliaryDevice = DeviceFactory.CreateClaimedDevice(
                            wanted,
                            this,
                            out this.auxiliaryDeviceClaim);

                        if (this.auxiliaryDevice != null)
                        {
                            if (!await this.auxiliaryDevice.Initialize())
                            {
                                this.AddUserMessage("Unable to initialize " + wanted.Describe() + ".");
                                this.ReleaseAuxiliaryDevice();
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        // Opening a port that is absent or held by something else throws rather than
                        // returning false, and that used to escape as far as the logging thread -
                        // where it stopped the PCM being logged at all.
                        this.AddUserMessage(
                            wanted.Describe() + " will not open, so the auxiliary bus will not be read: " +
                            exception.Message);
                        this.AddDebugMessage(exception.ToString());
                        this.ReleaseAuxiliaryDevice();
                    }
                }

                this.auxiliaryDeviceSelection = wanted;
            }

            if (this.auxiliaryDevice == null)
            {
                await busLogger.Start(null, BusProtocol.VPW);
                return;
            }

            // Whichever bus the interface can do. A device that offers both is put on CAN, since a
            // VPW auxiliary bus is the rarer case and the PCM interface is usually the VPW one.
            BusProtocol protocol = this.auxiliaryDevice.MonitorableProtocols.Contains(BusProtocol.Can500k)
                ? BusProtocol.Can500k
                : BusProtocol.VPW;

            try
            {
                await busLogger.Start(this.auxiliaryDevice, protocol);
            }
            catch (Exception exception)
            {
                this.AddUserMessage("The auxiliary bus will not be read: " + exception.Message);
                this.AddDebugMessage(exception.ToString());
            }
        }

        private void ReleaseAuxiliaryDevice()
        {
            this.auxiliaryDevice?.Dispose();
            this.auxiliaryDevice = null;

            this.auxiliaryDeviceClaim?.Dispose();
            this.auxiliaryDeviceClaim = null;
        }

        private async Task<LogSession> RecreateLogger(ParameterDatabase parameterDatabase)
        {
            // Any open still in flight from a previous rebuild, so two of them cannot race for the
            // same port. Usually already finished, and null on the first pass. Cleared before the
            // await, or a faulted task would be re-thrown by every rebuild after it.
            Task? pendingAuxiliaryBus = this.auxiliaryBusTask;
            this.auxiliaryBusTask = null;
            if (pendingAuxiliaryBus != null)
            {
                await pendingAuxiliaryBus;
            }

            if (this.auxiliaryBusLogger == null)
            {
                this.auxiliaryBusLogger = new AuxiliaryBusLogger(parameterDatabase, this);
            }

            // Configure the auxiliary source before the session starts polling the PCM again. A
            // live or recording session was paused by the logging thread for this rebuild and is
            // resumed below with its prior state.
            //
            // Only when the Auxiliary Bus option is enabled. A remembered interface is not consent
            // to open it: the auxiliary adapter and the PCM interface can be the same hardware - an
            // OBDX Pro selected on J2534 has a COM port underneath - and grabbing it produces an
            // access denial that reads as the app being unable to decide which interface it is using.
            this.auxiliaryBusTask = this.StartAuxiliaryBus(this.auxiliaryBusLogger);
            await this.auxiliaryBusTask;
            this.auxiliaryBusTask = null;

            // Re-checked after the awaits above: opening the auxiliary interface takes long enough
            // for an interface change to release the Vehicle in the meantime.
            Vehicle? vehicle = this.Vehicle;
            if (vehicle == null)
            {
                throw new LogStartFailedException("No interface is connected.");
            }

            // Source order is column order, so the PCM comes first and the auxiliary bus after it -
            // which is the order the log file and the display have always used.
            LogSession session = new LogSession(
                new IParameterSource[]
                {
                    new PcmParameterSource(vehicle, this.osid, this),
                    new AuxiliaryBusParameterSource(this.auxiliaryBusLogger, this.AuxiliaryBusEnabled),
                });

            if (!await session.Open(this.currentProfile.Columns.ToList(), CancellationToken.None))
            {
                session.Dispose();
                throw new LogStartFailedException();
            }

            await session.Start(CancellationToken.None);

            // Anything the PCM refused is gone from this session's log, so take it off the grid too.
            // Leaving it ticked would have it re-requested - and re-refused - on the next profile
            // change, and would quietly suggest it is being recorded when it is not.
            if (session.UnsupportedParameters.Count > 0)
            {
                IReadOnlyList<Parameter> unsupported = session.UnsupportedParameters;
                this.recordingStatus.Invoke((MethodInvoker)delegate ()
                {
                    this.UntickUnsupportedParameters(unsupported);
                });
            }

            return session;
        }

        private async Task<Tuple<LogFileWriter,StreamWriter>> StartSaving(LogSession session)
        {
            string[] columnNames = session.GetColumnNames().ToArray();
            bool append = this.recordedLogPath != null
                && File.Exists(this.recordedLogPath)
                && this.recordedColumnNames != null
                && this.recordedColumnNames.SequenceEqual(columnNames, StringComparer.Ordinal);

            string logFilePath;
            if (append)
            {
                logFilePath = this.recordedLogPath!;
            }
            else
            {
                logFilePath = this.GenerateLogFilePath();
                this.recordedLogPath = logFilePath;
                this.recordedStartTime = DateTime.Now;
                this.recordedColumnNames = columnNames;
                Interlocked.Exchange(ref this.recordedFrames, 0);
            }

            this.recordingFileOpen = true;
            StreamWriter streamWriter = new StreamWriter(logFilePath, append);
            LogFileWriter logFileWriter = new LogFileWriter(
                streamWriter,
                append ? this.recordedStartTime : null);

            if (!append)
            {
                await logFileWriter.WriteHeader(columnNames);
            }

            return new Tuple<LogFileWriter, StreamWriter>(logFileWriter, streamWriter);
        }

        private void StopSaving(ref StreamWriter? streamWriter)
        {
            if (streamWriter != null)
            {
                streamWriter.Dispose();
                streamWriter = null;
            }

            if (this.recordingFileOpen)
            {
                this.recordingFileOpen = false;
                this.ReportRecordingFinished();
            }
        }

        /// <summary>
        /// Tell the UI the file is closed, so Save can be offered.
        /// </summary>
        private void ReportRecordingFinished()
        {
            string? path = this.recordedLogPath;
            int frames = System.Threading.Volatile.Read(ref this.recordedFrames);

            if (this.logStopRequested || path == null)
            {
                return;
            }

            try
            {
                this.recordingStatus.Invoke((MethodInvoker)delegate ()
                {
                    this.recordingStatus.Text = frames == 0
                        ? "Nothing was recorded."
                        : $"Recorded {frames:N0} frames. Save Log to keep a copy.";

                    this.UpdateLogButtons();
                });
            }
            catch (Exception exception)
            {
                // The form may be on its way out; the file is closed either way.
                this.AddDebugMessage("Unable to report the end of recording: " + exception.Message);
            }
        }

        private async Task ProcessRow(LogSession session, LogFileWriter? logFileWriter)
        {
            // An incomplete row comes back empty rather than null.
            IReadOnlyList<LogRowElement> rowValues = await session.Read(CancellationToken.None);
            if (rowValues.Count > 0)
            {
                this.recordingStatus.Invoke(
                    (MethodInvoker)
                    delegate ()
                    {
                        this.AddDebugMessage("Row received.");
                    });

                // Hand this data off to be written to disk and displayed in the UI.
                this.logRowQueue.Enqueue(
                    new Tuple<LogFileWriter?, IReadOnlyList<LogRowElement>>(
                        logFileWriter,
                        rowValues));

                this.rowAvailable.Set();
            }
        }

        /// <summary>
        /// The loop that reads data from the PCM.
        /// </summary>
        private async void LoggingThread(object threadContext)
        {
            using (AwayMode lockScreenSuppressor = new AwayMode())
            {
                try
                {
                    // Start the write/display thread.
                    ThreadPool.QueueUserWorkItem(LogFileWriterThread, null);

#if VPW4x
                    if (!await this.Vehicle.VehicleSetVPW4x(VPWSpeed.FourX))
                    {
                        this.AddUserMessage("Unable to switch to 4x.");
                        return;
                    }
#endif

                    StreamWriter? streamWriter = null;
                    try
                    {
                        string? lastSession = null;
                        LogSession? logger = null;
                        LogFileWriter? logFileWriter = null;

                        // Back-off for a session that will not start. Rebuilding straight away is
                        // right for a transient cause, but a profile this PCM will never accept used
                        // to re-fail every few seconds for as long as the app was open.
                        int startFailures = 0;
                        DateTime nextStartAttempt = DateTime.MinValue;
                        string? failedSession = null;
                        string? reportedFailure = null;

                        while (!this.logStopRequested)
                        {
                            try
                            {
                                // Between rows is the only safe moment to hand the bus over: one
                                // iteration is at most one complete exchange, so nothing is left
                                // half-said. The session stays open and the file stays open, so
                                // logging picks up where it left off with a gap of a few rows.
                                if (this.busHandoverRequested)
                                {
                                    // Told to stop before the bus is declared free: a streaming PCM
                                    // keeps sending log rows until it is asked not to, and whatever
                                    // borrows the bus would read those instead of its own answers.
                                    if (logger != null)
                                    {
                                        await logger.Suspend();
                                    }

                                    this.busIdle.Set();

                                    while (this.busHandoverRequested && !this.logStopRequested)
                                    {
                                        Thread.Sleep(20);
                                    }

                                    this.busIdle.Reset();

                                    if (logger != null)
                                    {
                                        await logger.Resume();
                                    }

                                    continue;
                                }

                                // Selecting an interface releases the Vehicle while this thread is
                                // still running, so there is a window where there is nothing to log
                                // through. Idle until one is back rather than dereferencing it: that
                                // window is exactly as long as the device picker is open, and it used
                                // to end in a NullReferenceException inside RecreateLogger.
                                if (this.Vehicle == null)
                                {
                                    if (logger != null)
                                    {
                                        this.StopSaving(ref streamWriter);
                                        logFileWriter = null;
                                        logger.Dispose();
                                        logger = null;
                                        this.logState = LogState.Nothing;
                                    }

                                    // Rebuild when an interface comes back, even if the profile has
                                    // not changed, and without serving out a back-off earned before
                                    // it went away.
                                    lastSession = null;
                                    startFailures = 0;
                                    nextStartAttempt = DateTime.MinValue;
                                    failedSession = null;
                                    reportedFailure = null;

                                    Thread.Sleep(100);
                                    continue;
                                }

                                // Re-create the logger when the profile's CONTENT changes, not when
                                // the object does. The grid rebuilds the LogProfile every time
                                // anything touches it - including re-asserting the dashboard's locks
                                // - so comparing instances reconfigured the PCM constantly. With a
                                // parameter the PCM refuses that became a retry storm that filled
                                // the debug log faster than it could be read.
                                // Nothing is read until Start is pressed. Connecting an interface
                                // used to begin polling on its own, which put traffic on the bus
                                // while the user was still deciding what to look at - and left no
                                // way to hold the display still to inspect a loaded log.
                                if (!this.viewing)
                                {
                                    if (logger != null)
                                    {
                                        this.StopSaving(ref streamWriter);
                                        logFileWriter = null;
                                        logger.Dispose();
                                        logger = null;
                                        this.logState = LogState.Nothing;
                                    }

                                    lastSession = null;
                                    Thread.Sleep(100);
                                    continue;
                                }

                                string sessionSignature = SessionSignature(this.currentProfile);

                                if (sessionSignature != failedSession)
                                {
                                    // A different configuration gets an immediate attempt.
                                    startFailures = 0;
                                    nextStartAttempt = DateTime.MinValue;
                                    failedSession = null;
                                    reportedFailure = null;
                                }
                                else if (DateTime.Now < nextStartAttempt)
                                {
                                    Thread.Sleep(100);
                                    continue;
                                }

                                if (sessionSignature != lastSession)
                                {
                                    this.StopSaving(ref streamWriter);

                                    if ((this.currentProfile == null) || this.currentProfile.IsEmpty)
                                    {
                                        this.logState = LogState.Nothing;
                                        lastSession = sessionSignature;
                                        logger?.Dispose();
                                        logger = null;

                                        this.recordingStatus.Invoke(
                                            (MethodInvoker)
                                            delegate ()
                                            {
                                                this.startStopButton.Enabled = false;
                                                this.logValues.Text = "Please select some parameters, or open a log profile.";
                                            });
                                    }
                                    else
                                    {
                                        Exception? exception = null;

                                        try
                                        {
                                            // It may be counterintuitive that we update lastSession here, but that
                                            // prevents the invalid parameter exception from being thrown repeatedly.
                                            lastSession = sessionSignature;
                                            logger?.Dispose();
                                            logger = await this.RecreateLogger(this.database);

                                            // If this was the first profile to load...
                                            if (this.logState == LogState.Nothing)
                                            {
                                                this.logState = LogState.DisplayOnly;
                                            }

                                            switch (logState)
                                            {
                                                case LogState.Nothing:
                                                case LogState.DisplayOnly:
                                                case LogState.StopSaving:
                                                    break;

                                                default:
                                                    var tuple = await this.StartSaving(logger);
                                                    logFileWriter = tuple.Item1;
                                                    streamWriter = tuple.Item2;
                                                    logState = this.saving ? LogState.Saving : LogState.StopSaving;
                                                    break;
                                            }
                                        }
                                        catch (NeedMoreParametersException ex)
                                        {
                                            exception = ex;
                                        }
                                        catch (ParameterNotSupportedException ex)
                                        {
                                            exception = ex;
                                        }
                                        catch (LoggingNotSupportedException ex)
                                        {
                                            // lastSession stays set, so this is not retried.
                                            exception = ex;
                                        }
                                        catch (Exception ex)
                                        {
                                            // LogStartFailedException and anything unexpected:
                                            // recreated on the next iteration, but at a widening
                                            // interval rather than flat out. The unexpected used to
                                            // reach the outer handler, which left lastSession set
                                            // and no logger - so the session stayed dead until the
                                            // interface was re-picked.
                                            lastSession = null;
                                            failedSession = sessionSignature;
                                            startFailures++;
                                            nextStartAttempt = DateTime.Now.AddSeconds(
                                                Math.Min(30, 2 * startFailures));

                                            exception = ex;
                                        }
                                        finally
                                        {
                                            if (exception != null)
                                            {
                                                logState = LogState.Nothing;

                                                // The pane always shows the state; the log only
                                                // takes a line when the message changes.
                                                bool repeated = exception.Message == reportedFailure;
                                                reportedFailure = exception.Message;

                                                this.recordingStatus.Invoke(
                                                    (MethodInvoker)
                                                    delegate ()
                                                    {
                                                        if (!repeated)
                                                        {
                                                            this.AddUserMessage(exception.Message);
                                                        }

                                                        this.startStopButton.Enabled = false;
                                                        this.logValues.Text = exception.Message;
                                                    });
                                            }
                                            else
                                            {
                                                this.recordingStatus.Invoke(
                                                    (MethodInvoker)
                                                    delegate ()
                                                    {
                                                        this.startStopButton.Enabled = true;
                                                    });
                                            }
                                        }
                                    }
                                }

                                switch (logState)
                                {
                                    case LogState.Nothing:
                                        Thread.Sleep(100);
                                        break;

                                    case LogState.DisplayOnly:
                                        if (logger != null)
                                        {
                                            await this.ProcessRow(logger, null);
                                        }
                                        break;

                                    case LogState.StartSaving:
                                        if (logger != null)
                                        {
                                            var tuple = await this.StartSaving(logger);
                                            logFileWriter = tuple.Item1;
                                            streamWriter = tuple.Item2;
                                            logState = this.saving ? LogState.Saving : LogState.StopSaving;
                                        }
                                        break;

                                    case LogState.Saving:
                                        if (logger != null)
                                        {
                                            await this.ProcessRow(logger, logFileWriter);
                                        }
                                        break;

                                    case LogState.StopSaving:
                                        // Publish the idle state before closing the writer reports
                                        // that Save is ready. The UI can then safely start another
                                        // segment as soon as the file is closed.
                                        this.logState = LogState.DisplayOnly;
                                        this.StopSaving(ref streamWriter);
                                        logFileWriter = null;
                                        break;
                                }
                            }
                            catch (Exception exception)
                            {
                                this.AddUserMessage("Logging interrupted. " + exception.Message);
                                this.AddDebugMessage(exception.ToString());
                                this.logValues.Invoke(
                                    (MethodInvoker)
                                    delegate ()
                                    {
                                        this.logValues.Text = "Logging interrupted. " + exception.Message;
                                        this.startStopButton.Focus();
                                    });

                            }
                        }

                        this.logStopRequested = false;
                    }
                    finally
                    {
                        if (streamWriter != null)
                        {
                            streamWriter.Dispose();
                            streamWriter = null;
                        }

                        endWriterThread.Set();
                    }
                }
                catch (Exception exception)
                {

                    this.AddUserMessage("Logging broken. " + exception.Message);
                    this.AddDebugMessage(exception.ToString());
                    this.logValues.Invoke(
                        (MethodInvoker)
                        delegate ()
                        {
                            this.logValues.Text = "Logging broken. " + exception.Message;
                            this.startStopButton.Focus();
                        });
                }
                finally
                {
                    this.loggerThreadEnded.Set();
#if VPW4x
                    if (!await this.Vehicle.VehicleSetVPW4x(VPWSpeed.Standard))
                    {
                        // Try twice...
                        await this.Vehicle.VehicleSetVPW4x(VPWSpeed.Standard);
                    }
#endif
                }
            }
        }

        /// <summary>
        /// Background thread to write to disk and send updates to the UI.
        /// This minimizes the amount code that executes between requests for new rows of log data.
        /// </summary>
        private void LogFileWriterThread(object threadContext)
        {
            WaitHandle[] writerHandles = new WaitHandle[] { endWriterThread, rowAvailable };

            try
            {
                while (!logStopRequested)
                {
                    int index = WaitHandle.WaitAny(writerHandles);
                    if (index == 0)
                    {
                        this.BeginInvoke((MethodInvoker)
                        delegate ()
                        {
                            this.logValues.Text = "Logging halted.";
                        });

                        return;
                    }

                    Tuple<LogFileWriter?, IReadOnlyList<LogRowElement>> row;
                    if (logRowQueue.TryDequeue(out row))
                    {
                        // The file wants the formatted strings, in the order the columns were built;
                        // that is exactly the order of the elements.
                        string[] rowStrings = row.Item2.Select(x => x.ValueAsString).ToArray();

                        // Record the sample before anything is drawn. The monitors read from the
                        // history on their own timer rather than from this row, so logging never
                        // waits for the screen and the screen never has to keep up with logging.
                        this.AppendToHistory(row.Item2);

                        if (row.Item1 != null)
                        {
                            row.Item1.WriteLine(rowStrings);
                            Interlocked.Increment(ref this.recordedFrames);
                        }

                        Tuple<string, List<ZoomedParameter>> values = FormatValuesForDisplay(row.Item2);

                        // Publish and move on. Marshalling every row to the UI thread would queue
                        // work faster than it can be drawn once the PCM is answering quickly, and
                        // the window would stop responding while the backlog cleared. The display
                        // timer collects the most recent frame and the rest are dropped, which costs
                        // nothing: the file and the history already have every sample.
                        this.displayFeed.Publish(
                            new DisplayFrame(values.Item1, values.Item2, row.Item2));
                    }
                }
            }
            catch (Exception exception)
            {
                if (!logStopRequested)
                {
                    this.AddUserMessage("Log writing halted. " + exception.Message);
                    this.AddDebugMessage(exception.ToString());
                    this.logValues.Invoke(
                            (MethodInvoker)
                            delegate ()
                            {
                                this.logValues.Text = "Log writing halted. " + exception.Message;
                                this.startStopButton.Focus();
                            });
                }
            }
            finally
            {
                this.writerThreadEnded.Set();
            }
        }
    }
}

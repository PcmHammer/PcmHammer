// SPDX-License-Identifier: GPL-3.0-only
//#define VPW4x

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    public partial class MainForm : MainFormBase
    {
        private volatile bool saving;

        /// <summary>
        /// Whether the logging thread should be reading the vehicle. False until Start is pressed,
        /// so connecting an interface no longer puts traffic on the bus on its own.
        /// </summary>
        private volatile bool viewing;

        private object loggingLock = new object();
        private bool logStopRequested;
        private TaskScheduler uiThreadScheduler = null!;
        private uint osid;

        private const string AppName = "PCM Logger";
        private const string DefaultFileName = "New Profile";
        private string fileName = DefaultFileName;


        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            // Offer the interfaces that are good for logging, which includes listen-capable adapters
            // that are not offered for flashing. Set before any device picker is opened.
            DeviceCatalog.Use = DeviceUse.Logging;

            // Window and taskbar icon, taken from the executable's own icon so the image is not
            // duplicated into this form's resources.
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        #region MainFormBase override methods

        /// <summary>
        /// Not used.
        /// </summary>
        /// <param name="message"></param>
        public override void AddUserMessage(string message)
        {
            // The logger app doesn't have a good place for this kind of thing,
            // so messages are only sent to the debug pane. Important messages
            // should be displayed in the parameters pane, however that only
            // works if the logger is stopped, so it is done from the background
            // thread that handles logging.
            this.AddDebugMessage(message);
        }

        /// <summary>
        /// Add a message to the debug pane of the main window.
        /// </summary>
        public override void AddDebugMessage(string message)
        {
            // LogListView queues the line and does no UI work, so this is safe from the logging
            // threads without marshalling. The BeginInvoke this used to do was both a bottleneck
            // under a fast PCM and a hazard during shutdown.
            this.debugLog.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]  " + message);
        }

        public override void ResetLogs()
        {
            this.debugLog.ClearLog();
        }

        public override string GetAppNameAndVersion()
        {
            return "PCM Logger";
        }

        protected override void DisableUserInput()
        {
            this.EnableProfileButtons(false);
            this.profileList.Enabled = false;
            this.parameterGrid.Enabled = false;
            this.parameterSearch.Enabled = false;
            this.selectButton.Enabled = false;
            this.startStopButton.Enabled = false;
        }

        protected override void EnableInterfaceSelection()
        {
            this.selectButton.Enabled = true;
        }

        protected override void EnableUserInput()
        {
            this.EnableProfileButtons(true);
            this.profileList.Enabled = true;
            this.parameterGrid.Enabled = true;
            this.parameterSearch.Enabled = true;
            this.selectButton.Enabled = true;
            this.startStopButton.Enabled = true;
            this.startStopButton.Focus();
        }

        protected override void NoDeviceSelected()
        {
            this.selectButton.Enabled = true;
            this.deviceDescription.Text = "No device selected";

            // This or ValidDeviceSelected will be called after the form loads.
            // Either way, we should let users manipulate profiles.
            this.EnableProfileButtons(true);
        }

        protected override void SetSelectedDeviceText(string message)
        {
            this.deviceDescription.Text = message;
        }

        /// <summary>
        /// This is invoked from within the call to base.ResetDevice().
        /// </summary>
        protected override async Task ValidDeviceSelectedAsync(string deviceName)
        {
            this.AddDebugMessage("ValidDeviceSelectedAsync started.");

            // Probe for the PCM on every bus the interface supports rather than assuming VPW.
            // QueryOperatingSystemId, which this used to call, is the VPW-shaped query and never
            // changes bus - so a CAN PCM was silently polled on the wrong bus forever. Every other
            // front end already goes through here; this one was the exception.
            DetectedModule? pcm = await this.Vehicle.DetectAndSelectPcm(new CancellationToken());
            if (pcm == null)
            {
                // DetectAndSelectPcm already says once when the interface cannot reach a bus at all,
                // which is the other reason for silence and is worth distinguishing.
                this.Invoke((MethodInvoker)delegate ()
                {
                    this.deviceDescription.Text = deviceName + ": no PCM found on VPW or CAN";
                });

                return;
            }

            // This must be assigned prior to calling FillParameterGrid(),
            // otherwise the RAM parameters will not appear in the grid.
            this.osid = pcm.Osid;

            this.AddUserMessage($"Found a PCM on {pcm.Bus}, operating system {pcm.Osid}.");

            // Asked before the grid is built, so the rows can be drawn in their final state rather
            // than appearing and then greying out. Null when the PCM would not say, and then
            // nothing is disabled.
            HashSet<uint>? supportedPids = await this.Vehicle.ReadSupportedPids(new CancellationToken());

            this.Invoke((MethodInvoker)delegate ()
            {
                this.deviceDescription.Text = $"{deviceName} - {osid} on {pcm.Bus}";
                this.startStopButton.Enabled = true;
                this.parameterGrid.Enabled = true;
                this.EnableProfileButtons(true);

                // Finding a PCM is what used to begin reading it. That is still the default, but it
                // is now a choice: Options on the Configuration tab.
                if (this.AutoStartEnabled)
                {
                    this.viewing = true;
                    this.recordingStatus.Text = "Reading the vehicle.";
                }

                this.UpdateLogButtons();

                try
                {
                    this.FillParameterGrid();
                    this.FillBusParameterGrid();
                    this.DisableUnsupportedPids(supportedPids);

                    // Scroll bars have no window until there are rows to need one, so they are not
                    // there to be themed when the window is first painted.
                    this.ApplyTheme();
                }
                catch (Exception ex)
                {
                    this.AddUserMessage("Error Loading Parameter Database:" + ex.Message);
                    this.AddDebugMessage(ex.ToString());
                }
            });

            string lastProfile = Configuration.Settings.LastProfile;
            if (!string.IsNullOrEmpty(lastProfile) && File.Exists(lastProfile))
            {
                this.Invoke((MethodInvoker)delegate ()
                {
                    this.OpenProfile(lastProfile);
                });
            }

            // Start pulling data from the PCM
            this.logStopRequested = false;
            ThreadPool.QueueUserWorkItem(new WaitCallback(LoggingThread), null);

            this.AddDebugMessage("ValidDeviceSelectedAsync ended.");
        }

        #endregion

        #region Open / Close

        /// <summary>
        /// Open the most-recently-used device, if possible.
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            // Order matters - the scheduler must be set before adding messages.
            this.uiThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            this.AddDebugMessage("MainForm_Load started.");

            string logDirectory = Configuration.Settings.LogDirectory;
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                logDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                Configuration.Settings.LogDirectory = logDirectory;
                Configuration.Save(this);
            }

            // This just saves the trouble of having to keep a const string in 
            // sync with whatever window text is entered in the designer view.
            this.Text = AppName;

            this.EnableProfileButtons(false);

            // The Dash and Monitors tabs are built in code; see MainForm.Dashboard.cs.
            this.InitializeDashboardTabs();

            // After the tabs exist: this decides which of them are shown.
            this.InitializeViewOptions();

            // After the controls exist, so there is something to colour.
            this.ApplyTheme();

            this.UpdateLogButtons();

            this.LoadProfileHistory();

            // Auxiliary bus. Whether it is in use is the View toggle's business, and which interface
            // serves it is read from settings when the logger starts.
            this.EnableCanControls(this.AuxiliaryBusEnabled, false);

            // Begin logging
            ThreadPool.QueueUserWorkItem(BackgroundInitialization);

            this.logFilePath.Text = logDirectory;

            this.AddDebugMessage("MainForm_Load ended.");
        }

        private async void BackgroundInitialization(object unused)
        {
            try
            {
                this.AddDebugMessage("Device reset started.");

                // This will cause the ValidDeviceSelectedAsync callback to be invoked.
                await this.ResetDevice();

                this.AddDebugMessage("Device reset completed.");
            }
            catch (Exception exception)
            {
                // Don't try to log messages during shutdown, that doesn't end well
                // because the window handle is no longer valid.
                //
                // There is still a race condition around using logStopRequested for
                // this, but the only deterministic solution involves cross-thread 
                // access to the Form object, which isn't allowed.
                if (!this.logStopRequested)
                {
                    this.Invoke(
                        (MethodInvoker)
                        delegate ()
                        {
                            if (!this.logStopRequested)
                            {
                                this.AddDebugMessage("BackgroundInitialization: " + exception.ToString());
                            }
                        });
                }
            }
        }

        private void EnableProfileButtons(bool enable)
        {
            this.newButton.Enabled = enable;
            this.openButton.Enabled = enable;
            this.saveButton.Enabled = enable;
            this.saveAsButton.Enabled = enable;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.currentProfileIsDirty)
            {
                if (this.SaveIfNecessary() == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            this.logStopRequested = true;

            // Before the waits below, which pump messages: a display tick during them would draw on
            // panels that are on their way out.
            this.ShutdownDashboardTabs();

            this.SaveProfileHistory();

            // It turns out that WaitAll is not supported on an STA thread.
            // WaitHandle.WaitAll(new WaitHandle[] { loggerThreadEnded, writerThreadEnded });
            loggerThreadEnded.WaitOne(1000);
            writerThreadEnded.WaitOne(1000);

            // After those waits, so nothing is still using the device, and never left to the
            // garbage collector: a J2534 device closed from its finalizer calls into the vendor's
            // DLL during process teardown, which comes back as an AccessViolationException that no
            // catch block can stop. PcmHammer has always done this on the way out; this one did not.
            this.ReleaseVehicle();
        }

        #endregion

        #region Button clicks

        /// <summary>
        /// Select which interface device to use. This opens the Device-Picker dialog box.
        /// </summary>
        protected async void selectButton_Click(object sender, EventArgs e)
        {
            this.logStopRequested = true;
            await base.HandleSelectButtonClick();
        }

        /// <summary>
        /// Choose which directory to create log files in.
        /// </summary>
        private void setDirectory_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.SelectedPath = Configuration.Settings.LogDirectory;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Configuration.Settings.LogDirectory = dialog.SelectedPath;
                Configuration.Save(this);
                this.logFilePath.Text = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// Open a File Explorer window in the log directory.
        /// </summary>
        private void openDirectory_Click(object sender, EventArgs e)
        {
            Process.Start(Configuration.Settings.LogDirectory);
        }

        /// <summary>
        /// Start reading the car, or stop everything.
        /// </summary>
        /// <remarks>
        /// Stop ends recording as well as viewing, deliberately: one button means the end of the
        /// recording is where the user stopped looking, with nothing to reason about. The cost is
        /// that the on-screen history always holds more than the file - it starts when Start was
        /// pressed, not when Record was - which is the ambiguity at the other end of the session.
        /// </remarks>
        private async void startStopButton_Click(object sender, EventArgs e)
        {
            if (this.viewing)
            {
                this.StopViewing();
                return;
            }

            // A file opened for inspection is replaced by live data. A live capture, however,
            // belongs to the whole recording session and must survive disconnect/reconnect so the
            // next samples append at their real timestamps, leaving the offline gap visible.
            if (this.historyLoadedFromFile)
            {
                this.SetMonitorHistory(null);
                this.historyLoadedFromFile = false;
            }

            await this.ConnectToVehicle();
        }

        /// <summary>
        /// Find out what is on the bus, then start reading it.
        /// </summary>
        /// <remarks>
        /// Detection runs on every connect, not just when the interface is chosen: the car may have
        /// changed, and reusing the previous bus had the logger asking a VPW PCM for CAN parameters.
        /// Safe to use the bus here because the logging thread is parked until viewing is set below.
        /// </remarks>
        private async Task ConnectToVehicle()
        {
            Vehicle? vehicle = this.Vehicle;
            if (vehicle == null)
            {
                this.recordingStatus.Text = "No interface is connected.";
                return;
            }

            try
            {
                this.startStopButton.Enabled = false;
                this.recordingStatus.Text = "Looking for a PCM...";

                DetectedModule? pcm = await vehicle.DetectAndSelectPcm(new CancellationToken());
                if (pcm == null)
                {
                    this.recordingStatus.Text = "No PCM found on VPW or CAN.";
                    return;
                }

                this.AddUserMessage($"Found a PCM on {pcm.Bus}, operating system {pcm.Osid}.");
                this.deviceDescription.Text = $"{pcm.Osid} on {pcm.Bus}";

                // Only when it is a different PCM: rebuilding the grid throws away the ticks, and
                // reconnecting to the same car should not cost the user their parameter selection.
                if (pcm.Osid != this.osid)
                {
                    this.osid = pcm.Osid;
                    this.FillParameterGrid();
                    this.FillBusParameterGrid();
                    this.DisableUnsupportedPids(
                        await vehicle.ReadSupportedPids(new CancellationToken()));
                    this.ApplyTheme();
                }

                this.viewing = true;
                this.recordingStatus.Text = "Reading the vehicle.";
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to connect: " + exception.Message);
                this.AddDebugMessage(exception.ToString());
                this.recordingStatus.Text = "Unable to connect.";
            }
            finally
            {
                this.startStopButton.Enabled = true;
                this.UpdateLogButtons();
            }
        }

        /// <summary>
        /// Start writing what is being read to a file, or stop everything.
        /// </summary>
        /// <remarks>
        /// Stop ends only the file recording. The live session keeps reading, so the display and
        /// in-memory history continue while Save or Clear is available; Record can start another
        /// segment in the same file. Disconnect remains the action that stops live reading too.
        ///
        /// The button stays lit and changes word rather than greying out: greyed out says "not now"
        /// where the truth is "already running".
        /// </remarks>
        private void recordButton_Click(object sender, EventArgs e)
        {
            if (!this.viewing)
            {
                return;
            }

            if (this.saving)
            {
                this.saving = false;
                this.logState = LogState.StopSaving;
                this.UpdateLogButtons();
                return;
            }

            // Wait for StopSaving to close the previous writer before reopening the same file.
            if (this.recordingFileOpen)
            {
                return;
            }

            this.saving = true;
            this.recordingStatus.Text =
                "Recording frame " + System.Threading.Volatile.Read(ref this.recordedFrames).ToString("N0");
            this.logState = LogState.StartSaving;
            this.UpdateLogButtons();
        }

        private void StopViewing()
        {
            if (this.saving)
            {
                this.saving = false;
                this.logState = LogState.StopSaving;
            }

            this.viewing = false;
            this.UpdateLogButtons();
        }

        /// <summary>
        /// Put the buttons in step with what the app is doing.
        /// </summary>
        private void UpdateLogButtons()
        {
            this.startStopButton.Text = this.viewing ? "&Disconnect" : "&Connect";

            this.recordButton.Enabled = this.viewing && (this.saving || !this.recordingFileOpen);
            this.recordButton.Text = this.saving ? "&Stop" : "&Record";

            this.UpdateClearLogButton();

            // Loading replaces what is on screen, so it waits until nothing is arriving.
            this.loadLogButton.Enabled = !this.viewing;

            // Saving moves the file, so not until the writer has closed it.
            this.saveLogButton.Enabled = !this.saving && !this.recordingFileOpen
                && this.recordedLogPath != null;

            this.UpdateTitle();
        }

        private void UpdateClearLogButton()
        {
            this.clearLogButton.Enabled = !this.saving && !this.recordingFileOpen
                && this.history != null && this.history.Count > 0;
        }

        private void saveLogButton_Click(object sender, EventArgs e)
        {
            string? path = this.recordedLogPath;
            if (path == null)
            {
                return;
            }

            if (this.PromptToSaveLog(path, this.recordedFrames))
            {
                // Saved or discarded; there is nothing left here to offer.
                this.recordedLogPath = null;
                this.recordedColumnNames = null;
                System.Threading.Interlocked.Exchange(ref this.recordedFrames, 0);
                this.UpdateLogButtons();
            }
        }

        private void loadLogButton_Click(object sender, EventArgs e)
        {
            this.LoadLogForInspection();
        }

        /// <summary>Discard the captured in-memory history without affecting saved log files.</summary>
        private void clearLogButton_Click(object sender, EventArgs e)
        {
            LogHistory? captured = this.history;
            if (this.saving || this.recordingFileOpen || captured == null || captured.Count == 0)
            {
                return;
            }

            if (this.historyLoadedFromFile)
            {
                this.SetMonitorHistory(null);
                this.historyLoadedFromFile = false;
            }
            else
            {
                captured.Clear();
                this.SetMonitorHistory(captured);
            }
            this.UpdateMonitorTimeBox();
            this.recordingStatus.Text = "Captured log cleared.";
            this.UpdateLogButtons();
        }


        #endregion
    }
}

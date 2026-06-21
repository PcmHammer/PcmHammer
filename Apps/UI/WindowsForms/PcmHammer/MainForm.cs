// SPDX-License-Identifier: GPL-3.0-only
// Enable the nullable annotation context for this file so the '?' reference-type
// annotations below are valid. The project build sets this via MSBuild, but the
// legacy (non-SDK) project's IntelliSense engine does not reliably apply that
// setting, so the directive keeps the editor and the compiler in agreement.
#nullable enable annotations
using CommandLine;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    public partial class MainForm : MainFormBase, ILogger
    {
        /// <summary>
        /// This warning will be shown to users who have not yet verified their connectivity.
        /// </summary>
        /// <remarks>
        /// Users can verify connectivity by completing a successful test write, real write, or read.
        /// The message just encourages users to do a full read, because that's the best test.
        /// </remarks>
        private static readonly string UnverifiedConnectionWarning =
            "{0}" +
            Environment.NewLine + Environment.NewLine +
            "If this doesn't work, your vehicle will not be driveable." +
            Environment.NewLine + Environment.NewLine +
            "You should read the contents of your PCM before you try this. " +
            "A successful read will prove that you have a good " +
            "connection to your PCM, and will determine whether " +
            "or not there are any other modules in the vehicle " +
            "that will cause the write process to fail." +
            Environment.NewLine + Environment.NewLine +
            "A successful read will also give you a file that you can use to replace your current PCM with a new one if something goes wrong." +
            Environment.NewLine + Environment.NewLine +
            "It is dangerous to attempt to modify the PCM's flash memory before completing a successful read of the PCM.";

        /// <summary>
        /// Title for the unverified-connect warning prompt.
        /// </summary>
        private static readonly string UnverifiedConnectionWarningTitle = "Are you sure you want to do this?";

        /// <summary>
        /// Remind the user how to verify their connection.
        /// </summary>
        private static readonly string WiseChoice = "You have made a wise choice. Try a full read first.";

        /// <summary>
        /// Simple prompt for users who have already verified their connection.
        /// </summary>
        private static readonly string ClickOkToContinue = "Click OK to continue.";

        /// <summary>
        /// This will become the first half of the Window caption, and will 
        /// be printed to the user and debug logs each time a device is 
        /// initialized.
        /// </summary>
        private const string AppName = "PCM Hammer";

        /// <summary>
        /// We had to move some operations to a background thread for the J2534 code as the DLL functions do not have an awaiter.
        /// </summary>
        private System.Threading.Thread BackgroundWorker = new System.Threading.Thread(delegate () { return; });

        /// <summary>
        /// This flag will initialized when a long-running operation begins. 
        /// It will be toggled if the user clicks the cancel button.
        /// Long-running operations can abort when this flag changes.
        /// </summary>
        private CancellationTokenSource? cancellationTokenSource;

        /// <summary>
        /// Indicates what type of write, if any, is in progress.
        /// </summary>
        private WriteType currentWriteType = WriteType.None;

        /// <summary>
        /// Initializes a new instance of the main window.
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Add a message to the main window.
        /// </summary>
        public override void AddUserMessage(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]  " + message;

            // AppendLine is thread-safe and does no UI work, so the worker thread is never blocked.
            this.userLog.AppendLine(line);

            // User messages go to the debug log too, so the debug log has everything.
            this.debugLog.AppendLine(line);
        }

        /// <summary>
        /// Add a message to the debug pane of the main window.
        /// </summary>
        public override void AddDebugMessage(string message)
        {
            this.debugLog.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]  " + message);
        }

        public override void StatusUpdateActivity(string activity)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.activityToolStripStatusLabel.Text = activity;
                });
        }

        public override void StatusUpdateTimeRemaining(string remaining)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.timeRemainingToolStripStatusLabel.Text = remaining;
                });
        }

        public override void StatusUpdatePercentDone(string percent)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.percentDoneToolStripStatusLabel.Text = percent;
                });
        }

        public override void StatusUpdateRetryCount(string retries)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.retryCountToolStripStatusLabel.Text = retries;
                });
        }

        public override void StatusUpdateProgressBar(double completed, bool visible)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    if (visible)
                    {
                        this.progressBarToolStripProgressBar.Visible = true;
                    }
                    else
                    {
                        this.progressBarToolStripProgressBar.Visible = false;
                    }

                    this.progressBarToolStripProgressBar.Value = (int)(completed * 100);
                });
        }

        public override void StatusUpdateKbps(string Kbps)
        {
            this.statusStatusStrip.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.kbpsToolStripStatusLabel.Text = Kbps;
                });
        }

        public override void StatusUpdateReset()
        {
            this.StatusUpdateActivity(string.Empty);
            this.StatusUpdateTimeRemaining(string.Empty);
            this.StatusUpdatePercentDone(string.Empty);
            this.StatusUpdateRetryCount(string.Empty);
            this.StatusUpdateProgressBar(0, false);
            this.StatusUpdateKbps(string.Empty);
        }

        /// <summary>
        /// Reset the user and debug logs.
        /// </summary>
        public override void ResetLogs()
        {
            this.userLog.Invoke(
                (MethodInvoker)delegate ()
                {
                    this.userLog.ClearLog();
                    this.debugLog.ClearLog();
                });
        }

        private LogSearchBar searchBar;

        // The log shown on the active tab (Debug Log, else Results) is the one Ctrl+F searches.
        private LogListView ActiveLog() => this.tabs.SelectedTab == this.debugTab ? this.debugLog : this.userLog;

        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                this.ShowSearchBar();
                return true;
            }

            if (keyData == Keys.Escape && this.searchBar != null && this.searchBar.Visible)
            {
                this.HideSearchBar();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowSearchBar()
        {
            if (this.searchBar == null)
            {
                this.searchBar = new LogSearchBar { Anchor = AnchorStyles.Top | AnchorStyles.Right };
                this.searchBar.QueryChanged += _ => this.RunSearch();
                this.searchBar.FindNext += () => this.StepSearch(+1);
                this.searchBar.FindPrevious += () => this.StepSearch(-1);
                this.searchBar.CloseRequested += this.HideSearchBar;
                this.Controls.Add(this.searchBar);
                this.tabs.SelectedIndexChanged += (s, e) => { if (this.searchBar != null && this.searchBar.Visible) { this.RunSearch(); } };
            }

            this.searchBar.Location = new Point(this.tabs.Right - this.searchBar.Width - 6, this.tabs.Top + 6);
            this.searchBar.Visible = true;
            this.searchBar.BringToFront();
            this.searchBar.FocusInput();
            this.RunSearch();
        }

        private void HideSearchBar()
        {
            if (this.searchBar != null)
            {
                this.searchBar.Visible = false;
            }

            this.userLog.ClearSearch();
            this.debugLog.ClearSearch();
            this.ActiveLog().Focus();
        }

        // Run the current query against the active log and jump to the first match.
        private void RunSearch()
        {
            LogListView log = this.ActiveLog();
            int total = log.FindAll(this.searchBar.Query);
            if (total > 0)
            {
                log.MoveToMatch(+1);
            }

            this.searchBar.SetStatus(log.CurrentMatchNumber, total);
        }

        private void StepSearch(int direction)
        {
            LogListView log = this.ActiveLog();
            log.MoveToMatch(direction);
            this.searchBar.SetStatus(log.CurrentMatchNumber, log.MatchCount);
        }

        /// <summary>
        /// Invoked when a device is selected but NOT successfully initalized.
        /// </summary>
        protected override void NoDeviceSelected()
        {
            this.deviceDescription.Text = "No device selected.";
        }

        /// <summary>
        /// Invoked when a device is selected and successfully initialized.
        /// </summary>
        protected override Task ValidDeviceSelectedAsync(string deviceName)
        {
            this.Invoke((MethodInvoker)delegate ()
            {
                this.deviceDescription.Text = deviceName;
            });

            return Task.CompletedTask;
        }

        protected override void SetSelectedDeviceText(string message)
        {
            this.deviceDescription.Text = message;
        }

        /// <summary>
        /// Show the save-as dialog box (after a full read has completed).
        /// </summary>
        private string? ShowSaveAsDialog()
        {
            string? fileName = null;

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.DefaultExt = ".bin";
                dialog.Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*";
                dialog.FilterIndex = 1;
                dialog.OverwritePrompt = true;
                dialog.ValidateNames = true;
                dialog.RestoreDirectory = true;

                if (!string.IsNullOrWhiteSpace(Configuration.Settings.BinDirectory))
                {
                    dialog.InitialDirectory = Configuration.Settings.BinDirectory;
                }

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    fileName = dialog.FileName;
                }
            }
            return fileName;
        }

        /// <summary>
        /// Show the file-open dialog box, so the user can choose the file to write to the flash.
        /// </summary>
        private string? ShowOpenDialog()
        {
            string? fileName = null;

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.DefaultExt = ".bin";
                dialog.Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*";
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;

                if (!string.IsNullOrWhiteSpace(Configuration.Settings.BinDirectory))
                {
                    dialog.InitialDirectory = Configuration.Settings.BinDirectory;
                }

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    fileName = dialog.FileName;
                }
            }
            return fileName;
        }

        /// <summary>
        /// Generate a filename based on Log Name and Timestamp.
        /// </summary>
        /// <remarks>
        /// i.e. userLog.Name or debugLog.Name
        /// </remarks>
        private string GetLogFilename(string logName)
        {
            string fileName =
                "PcmHammer_"
                + logName
                + "_"
                + DateTime.Now.ToString("yyyyMMdd@HHmmss")
                + ".txt";
            return fileName;
        }

        /// <summary>
        /// Show a save-as dialog box for saving log files.
        /// </summary>
        /// <remarks>
        /// i.e. userLog.Name or debugLog.Name
        /// </remarks>
        private string ShowLogSaveAsDialog(string logName)
        {
            string fileName = string.Empty;

            if (!Configuration.Settings.UseLogSaveAsDialog)
            {
                return Configuration.Settings.LogDirectory + "\\" + GetLogFilename(logName);
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;
                dialog.InitialDirectory = Configuration.Settings.LogDirectory;
                dialog.FileName = GetLogFilename(logName);

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    fileName = dialog.FileName;
                }
            }
            return fileName;
        }

        /// <summary>
        /// Gets a string to use in the window caption and at the top of each log.
        /// </summary>
        public override string GetAppNameAndVersion()
        {
            return AppInfo.GetNameAndVersion(AppName, Generated.BuildTime);
        }

        /// <summary>
        /// Save the selected log
        /// </summary>
        protected void SaveLog(LogListView logBox, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            if (logBox.IsEmpty)
            {
                return;
            }

            try
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(fileName))
                {
                    file.WriteLine(logBox.GetAllText());
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, fileName,
                    "ERROR file NOT Saved." + Environment.NewLine + e.Message,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }


        /// <summary>
        /// Called when the main window is being created.
        /// </summary>
        private async void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                this.Text = GetAppNameAndVersion().Replace('\n', ' ');
                this.interfaceBox.Enabled = true;
                this.operationsBox.Enabled = true;

                // This will be enabled during full reads (but not writes)
                this.cancelButton.Enabled = false;

                // Load the dynamic content asynchronously.
                ThreadPool.QueueUserWorkItem(new WaitCallback(LoadStartMessage));
                ThreadPool.QueueUserWorkItem(new WaitCallback(LoadHelp));
                ThreadPool.QueueUserWorkItem(new WaitCallback(LoadCredits));

                this.MinimumSize = new Size(800, 600);

                if (string.IsNullOrWhiteSpace(Configuration.Settings.LogDirectory) || !Directory.Exists(Configuration.Settings.LogDirectory))
                {
                    Configuration.Settings.LogDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    Configuration.Settings.Save();
                }

                if (Configuration.Settings.MainWindowPersistence)
                {
                    if (Configuration.Settings.MainWindowSize.Width > 0 || Configuration.Settings.MainWindowSize.Height > 0)
                    {
                        this.WindowState = Configuration.Settings.MainWindowState;
                        if (this.WindowState == FormWindowState.Minimized)
                        {
                            this.WindowState = FormWindowState.Normal;
                        }
                        this.Location = Configuration.Settings.MainWindowLocation;
                        this.Size = Configuration.Settings.MainWindowSize;
                    }
                }

                this.StatusUpdateReset();

                ProcessCommandLine();

                await this.ResetDevice();
            }
            catch (Exception exception)
            {
                this.AddUserMessage(exception.Message);
                this.AddDebugMessage(exception.ToString());
            }
        }

        /// <summary>
        /// Parse cmdline parameters
        /// </summary>
        private void ProcessCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsed<CommandLineOptions>(o =>
                {
                    if (o.BinFilePath != null)
                    {
                        WriteCalibration(o.BinFilePath);
                    }

                    if (o.ShowVersion)
                    {
                        Console.WriteLine(GetAppNameAndVersion());
                    }

                    if (o.ResetDeviceConfiguration)
                    {
                        ResetDeviceConfiguration();
                    }
                });
        }

        /// <summary>
        /// Reset Device Configuration
        /// </summary>
        /// <remarks>
        /// Used by command line argument '-r' to reset device configuration.
        /// </remarks>
        private void ResetDeviceConfiguration()
        {
            DeviceConfiguration.Settings.DeviceCategory = string.Empty;
            DeviceConfiguration.Settings.SerialPortDeviceType = string.Empty;
            DeviceConfiguration.Settings.J2534DeviceType = string.Empty;
            DeviceConfiguration.Settings.SerialPort = string.Empty;
            DeviceConfiguration.Settings.Save();
        }

        /// <summary>
        /// Write calibration automatically after program start, if cmdline parameter 
        /// "writecalibration" with filename is detected
        /// </summary>
        private async void WriteCalibration(string BinFilePath)
        {
            if (!writeCalibrationButton.Enabled)
            {
                await HandleSelectButtonClick();
            }
            if (writeCalibrationButton.Enabled)
            {
                BackgroundWorker = new System.Threading.Thread(() => write_BackgroundThread(WriteType.Calibration, BinFilePath));
                BackgroundWorker.IsBackground = true;
                BackgroundWorker.Start();
            }
            else
            {
                this.AddUserMessage("No device configured");
            }
        }

        /// <summary>
        /// The startup message is loaded after the window appears, so that it doesn't slow down app initialization.
        /// </summary>
        private async void LoadStartMessage(object unused)
        {
            ContentLoader loader = new ContentLoader("start.txt", null, Assembly.GetExecutingAssembly(), this);
            using (Stream? content = await loader.GetContentStream())
            {
                try
                {
                    StreamReader reader = new StreamReader(content);
                    string message = reader.ReadToEnd();
                    this.AddUserMessage(message);
                }
                catch (Exception exception)
                {
                    this.AddDebugMessage("Unable to display startup message: " + exception.ToString());
                }
            }
        }

        /// <summary>
        /// The Help page is loaded after the window appears, so that it doesn't slow down app initialization.
        /// </summary>
        private async void LoadHelp(object unused)
        {
            ContentLoader loader = new ContentLoader("help.html", null, Assembly.GetExecutingAssembly(), this);
            Stream? content = await loader.GetContentStream();
            this.helpWebBrowser.Invoke(
                (MethodInvoker)delegate ()
                {
                    try
                    {
                        this.helpWebBrowser.DocumentStream = content;
                    }
                    catch (Exception exception)
                    {
                        this.AddDebugMessage("Unable to load help content: " + exception.ToString());
                    }
                });
        }

        /// <summary>
        /// The credits page is loaded after the window appears, so that it doesn't slow down app initialization.
        /// </summary>
        private async void LoadCredits(object unused)
        {
            ContentLoader loader = new ContentLoader("credits.html", null, Assembly.GetExecutingAssembly(), this);
            Stream? content = await loader.GetContentStream();
            this.helpWebBrowser.Invoke(
                (MethodInvoker)delegate ()
                {
                    try
                    {
                        this.creditsWebBrowser.DocumentStream = content;
                    }
                    catch (Exception exception)
                    {
                        this.AddDebugMessage("Unable load content for Credits tab: " + exception.ToString());
                    }
                });
        }

        /// <summary>
        /// Discourage users from closing the app during a write.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.cancellationTokenSource != null)
            {
                MessageBox.Show(
                    this,
                    "There is an operation in progress. End this before exiting the app.",
                    "PCM Hammer",
                    MessageBoxButtons.OK);
                e.Cancel = true;
                return;
            }

            switch (this.currentWriteType)
            {
                case WriteType.None:
                case WriteType.TestWrite:
                    break;

                default:
                    DialogResult choice = MessageBox.Show(
                        this,
                        "Closing PCM Hammer now could make your PCM unusable." + Environment.NewLine +
                        "Are you sure you want to take that risk?",
                        "PCM Hammer",
                        MessageBoxButtons.YesNo);

                    if (choice == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                    break;
            }

            if (Configuration.Settings.MainWindowPersistence)
            {
                Configuration.Settings.MainWindowState = this.WindowState;
                if (this.WindowState == FormWindowState.Normal)
                {
                    Configuration.Settings.MainWindowLocation = this.Location;
                    Configuration.Settings.MainWindowSize = this.Size;
                }
                else
                {
                    Configuration.Settings.MainWindowLocation = this.RestoreBounds.Location;
                    Configuration.Settings.MainWindowSize = this.RestoreBounds.Size;
                }
                Configuration.Settings.Save();
            }

            if (Configuration.Settings.SaveUserLogOnExit)
            {
                string fileName = Configuration.Settings.LogDirectory + "\\" + GetLogFilename(userLog.Name);
                SaveLog(this.userLog, fileName);
            }

            if (Configuration.Settings.SaveDebugLogOnExit)
            {
                string fileName = Configuration.Settings.LogDirectory + "\\" + GetLogFilename(debugLog.Name);
                SaveLog(this.debugLog, fileName);
            }

            this.Vehicle?.Dispose();
        }

        /// <summary>
        /// Disable buttons during a long-running operation (like reading or writing the flash).
        /// </summary>
        protected override void DisableUserInput()
        {
            this.interfaceBox.Enabled = false;

            // The operation buttons have to be enabled/disabled individually
            // (rather than via the parent GroupBox) because we sometimes want
            // to enable the re-initialize operation while the others are disabled.
            this.readEntirePCMToolStripMenuItem.Enabled = false;
            this.verifyEntirePCMToolStripMenuItem.Enabled = false;
            this.modifyVINToolStripMenuItem.Enabled = false;
            this.writeParmetersCloneToolStripMenuItem.Enabled = false;
            this.writeOSCalibrationBootToolStripMenuItem.Enabled = false;
            this.writeFullToolStripMenuItem.Enabled = false;
            this.settingsToolStripMenuItem.Enabled = false;
            this.saveToolStripMenuItem.Enabled = false;
            this.exitApplicationToolStripMenuItem.Enabled = false;
            this.userDefinedKeyToolStripMenuItem.Enabled = false;
            this.bruteForceUnlockToolStripMenuItem.Enabled = false;
            this.haltRunningKernelToolStripMenuItem.Enabled = false;
            this.testFileChecksumsToolStripMenuItem.Enabled = false;

            this.readPropertiesButton.Enabled = false;
            this.readPcmButton.Enabled = false;
            this.verifyPcmButton.Enabled = false;

            this.testWriteButton.Enabled = false;
            this.writeCalibrationButton.Enabled = false;
            this.exitKernelButton.Enabled = false;
            this.reinitializeButton.Enabled = false;
        }

        /// <summary>
        /// Enable the buttons when a long-running operation completes.
        /// </summary>
        protected override void EnableUserInput()
        {
            this.Invoke((MethodInvoker)delegate ()
            {
                this.interfaceBox.Enabled = true;

                // The operation buttons have to be enabled/disabled individually
                // (rather than via the parent GroupBox) because we sometimes want
                // to enable the re-initialize operation while the others are disabled.
                this.readEntirePCMToolStripMenuItem.Enabled = true;
                this.verifyEntirePCMToolStripMenuItem.Enabled = true;
                this.modifyVINToolStripMenuItem.Enabled = true;
                this.writeParmetersCloneToolStripMenuItem.Enabled = true;
                this.writeOSCalibrationBootToolStripMenuItem.Enabled = true;
                this.writeFullToolStripMenuItem.Enabled = true;
                this.settingsToolStripMenuItem.Enabled = true;
                this.saveToolStripMenuItem.Enabled = true;
                this.exitApplicationToolStripMenuItem.Enabled = true;
                this.userDefinedKeyToolStripMenuItem.Enabled = true;
                this.bruteForceUnlockToolStripMenuItem.Enabled = true;
                this.haltRunningKernelToolStripMenuItem.Enabled = true;
                this.testFileChecksumsToolStripMenuItem.Enabled = true;

                this.readPropertiesButton.Enabled = true;
                this.readPcmButton.Enabled = true;
                this.verifyPcmButton.Enabled = true;

                this.testWriteButton.Enabled = true;
                this.writeCalibrationButton.Enabled = true;
                this.exitKernelButton.Enabled = true;
                this.reinitializeButton.Enabled = true;
            });
        }

        protected override void EnableInterfaceSelection()
        {
            this.interfaceBox.Enabled = true;
            this.settingsToolStripMenuItem.Enabled = true;
            this.exitApplicationToolStripMenuItem.Enabled = true;

            // Test File Checksums works on a file only - it needs no interface - so make it
            // available whenever no operation is in progress, even with no device selected.
            this.testFileChecksumsToolStripMenuItem.Enabled = true;
        }

        /// <summary>
        /// Save Debug Log
        /// </summary>
        private void saveDebugLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string fileName = ShowLogSaveAsDialog(debugLog.Name);
            SaveLog(debugLog, fileName);
        }

        /// <summary>
        /// Save Results Log
        /// </summary>
        private void saveResultsLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string fileName = ShowLogSaveAsDialog(userLog.Name);
            SaveLog(userLog, fileName);
        }

        /// <summary>
        /// Exit Application
        /// </summary>
        private void exitApplicationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        /// <summary>
        /// Settings Dialog
        /// </summary>
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (DialogBoxes.SettingsDialogBox settingsDialog = new DialogBoxes.SettingsDialogBox())
            {
                DialogResult dialogResult = settingsDialog.ShowDialog();
            }
        }

        /// <summary>
        /// User Defined Key
        /// </summary>
        private void userDefinedKeyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (userDefinedKeyToolStripMenuItem.Checked)
            {
                using (DialogBoxes.UserDefinedKeyDialogBox keyDialog = new DialogBoxes.UserDefinedKeyDialogBox())
                {
                    DialogResult dialogResult = keyDialog.ShowDialog();
                    if (dialogResult == DialogResult.OK)
                    {
                        this.Vehicle.UserDefinedKey = keyDialog.UserDefinedKey;
                    }
                    else
                    {
                        this.userDefinedKeyToolStripMenuItem.Checked = false;
                    }
                }
            }
            else
            {
                this.Vehicle.UserDefinedKey = -1;
            }
        }

        /// <summary>
        /// Brute Force - open the dialog that sweeps the known key algorithms and/or tries
        /// numeric key values until the PCM unlocks.
        /// </summary>
        private void bruteForceUnlockToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (BackgroundWorker.IsAlive || this.Vehicle == null)
            {
                return;
            }

            // Show modeless so the user can switch between the Results and Debug Log tabs on the
            // main window while the search runs. The brute forcer does its device I/O on a background
            // task, so the UI thread stays responsive. We disable the operation controls (but not the
            // log tabs) for the dialog's lifetime so nothing competes for the device.
            this.DisableUserInput();
            DialogBoxes.BruteForceDialogBox dialog = new DialogBoxes.BruteForceDialogBox(this.Vehicle, this);
            dialog.FormClosed += (s, args) =>
            {
                this.EnableUserInput();
                dialog.Dispose();
            };
            dialog.Show(this);
        }

        /// <summary>
        /// Select which interface device to use. This opens the Device-Picker dialog box.
        /// </summary>
        private async void selectButton_Click(object sender, EventArgs e)
        {
            await this.HandleSelectButtonClick();
        }

        /// <summary>
        /// Reset the current interface device.
        /// </summary>
        private async void reinitializeButton_Click(object sender, EventArgs e)
        {
            await this.InitializeCurrentDevice();
        }
        
        /// <summary>
        /// Read the VIN, OS, etc.
        /// </summary>
        private async void readPropertiesButton_Click(object sender, EventArgs e)
        {
            if (this.Vehicle == null)
            {
                // This shouldn't be possible - it would mean the buttons 
                // were enabled when they shouldn't be.
                return;
            }

            try
            {
                this.DisableUserInput();
                DetectedModule? pcm = await this.Vehicle.DetectAndSelectPcm(CancellationToken.None);
                if (pcm == null)
                {
                    this.AddUserMessage("No PCM detected.");
                    return;
                }

                this.AddUserMessage("Detected PCM on " + pcm.Bus.ToString());

                if (pcm.Bus != BusProtocol.Vpw)
                {
                    this.AddUserMessage("Parameters:");
                    foreach (string line in await CanProperties.Read(this.Vehicle.CreateCanCommands(), CancellationToken.None))
                    {
                        this.AddUserMessage(line);
                    }
                    return;
                }

                OSIDInfo pcmInfo = new OSIDInfo(pcm.Osid);
                this.AddUserMessage("OSID: " + pcm.Osid.ToString());
                this.AddUserMessage("Description: " + pcmInfo.Description);

                var vinResponse = await this.Vehicle.QueryVin();
                if (vinResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("VIN: " + vinResponse.Value);
                }
                else
                {
                    this.AddUserMessage("VIN query failed: " + vinResponse.Status.ToString());
                }

                // Disable Calibration ID lookup for those that do not provide it
                if (pcmInfo.HardwareType != PcmType.BlackBox)
                {

                    var calResponse = await this.Vehicle.QueryCalibrationId();
                    if (calResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Calibration ID: " + calResponse.Value.ToString());
                    }
                    else
                    {
                        this.AddUserMessage("Calibration ID query failed: " + calResponse.Status.ToString());
                    }
                }

                // Disable HardwareID lookup for the P05, P10, P12 and E54.
                if (pcmInfo.HardwareType != PcmType.P05 && pcmInfo.HardwareType != PcmType.P05b && pcmInfo.HardwareType != PcmType.P10 && pcmInfo.HardwareType != PcmType.P12 && pcmInfo.HardwareType != PcmType.E54)
                {
                    var hardwareResponse = await this.Vehicle.QueryHardwareId();
                    if (hardwareResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Hardware ID: " + hardwareResponse.Value.ToString());
                    }
                    else
                    {
                        this.AddUserMessage("Hardware ID query failed: " + hardwareResponse.Status.ToString());
                    }
                }

                // Disable Serial Number lookup for those that do not provide it
                if (pcmInfo.HardwareType != PcmType.BlackBox)
                {
                    var serialResponse = await this.Vehicle.QuerySerial();

                    if (serialResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Serial Number: " + serialResponse.Value.ToString());
                    }
                    else
                    {
                        this.AddUserMessage("Serial Number query failed: " + serialResponse.Status.ToString());
                    }
                }

                // Disable BCC lookup for those that do not provide it
                if (pcmInfo.HardwareType != PcmType.P04 && pcmInfo.HardwareType != PcmType.P04_Early && pcmInfo.HardwareType != PcmType.P08)
                {
                    var bccResponse = await this.Vehicle.QueryBCC();
                    if (bccResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Broad Cast Code: " + bccResponse.Value.ToString());
                    }
                    else
                    {
                        this.AddUserMessage("BCC query failed: " + bccResponse.Status.ToString());
                    }
                }

                var mecResponse = await this.Vehicle.QueryMEC();
                if (mecResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("MEC: " + mecResponse.Value.ToString());
                }
                else
                {
                    this.AddUserMessage("MEC query failed: " + mecResponse.Status.ToString());
                }

                var voltageResponse = await this.Vehicle.QueryVoltage();
                if (voltageResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("Voltage: " + voltageResponse.Value.ToString());
                }
                else
                {
                    this.AddUserMessage("Voltage query failed: " + voltageResponse.Status.ToString());
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage(exception.Message);
                this.AddDebugMessage(exception.ToString());
            }
            finally
            {
                this.EnableUserInput();
            }
        }

        /// <summary>
        /// Update the VIN.
        /// </summary>
        private async void modifyVinButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Detect the bus and select its protocol first (parity with Read Properties); CAN
                // PCMs use the GMLAN VIN write path.
                DetectedModule? pcm = await this.Vehicle.DetectAndSelectPcm(CancellationToken.None);
                if (pcm == null)
                {
                    this.AddUserMessage("No PCM detected.");
                    return;
                }

                if (pcm.Bus == BusProtocol.Can500k)
                {
                    await this.ModifyVinCan();
                    return;
                }

                Response<uint> osidResponse = await this.Vehicle.QueryOperatingSystemId(CancellationToken.None);
                if (osidResponse.Status != ResponseStatus.Success)
                {
                    this.AddUserMessage("Operating system query failed: " + osidResponse.Status);
                    return;
                }

                OSIDInfo info = new OSIDInfo(osidResponse.Value);

                var vinResponse = await this.Vehicle.QueryVin();
                if (vinResponse.Status != ResponseStatus.Success)
                {
                    this.AddUserMessage("VIN query failed: " + vinResponse.Status.ToString());
                    return;
                }

                DialogBoxes.VinForm vinForm = new DialogBoxes.VinForm();
                vinForm.Vin = vinResponse.Value;
                DialogResult dialogResult = vinForm.ShowDialog();

                if (dialogResult == DialogResult.OK)
                {
                    bool unlocked = await this.Vehicle.UnlockEcu(info.KeyAlgorithm);
                    if (!unlocked)
                    {
                        this.AddUserMessage("Unable to unlock PCM.");
                        return;
                    }

                    Response<bool> vinmodified = await this.Vehicle.UpdateVin(vinForm.Vin.Trim());
                    if (vinmodified.Value)
                    {
                        this.AddUserMessage("VIN successfully updated to " + vinForm.Vin);
                        MessageBox.Show(
                            "VIN updated to " + vinForm.Vin + " successfully.\n\n" +
                            "Turn ignition off while leaving power connected for a few seconds to finish the save to flash.",
                            "Good news.", MessageBoxButtons.OK);
                    }
                    else
                    {
                        MessageBox.Show("Unable to change the VIN to " + vinForm.Vin + ". Error: " + vinmodified.Status, "Bad news.", MessageBoxButtons.OK);
                    }
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("VIN change failed: " + exception.ToString());
            }
        }

        /// <summary>
        /// Change the VIN on a CAN PCM (E38): read the current VIN (1A 90), prompt for the new one,
        /// unlock the PCM (seed/key), then write it (3B 90 + 17 ASCII bytes).
        /// </summary>
        private async Task ModifyVinCan()
        {
            OSIDInfo pcmInfo = new OSIDInfo(PcmType.E38);
            CanCommands commands = this.Vehicle.CreateCanCommands();

            Response<byte[]> vinResponse = await commands.ReadDataByIdentifier(Gmlan.VinDataIdentifier, CancellationToken.None);
            string? currentVin = DecodeCanVin(vinResponse);
            if (currentVin == null)
            {
                this.AddUserMessage("VIN query failed: " + vinResponse.Status.ToString());
                return;
            }
            this.AddUserMessage("VIN: " + currentVin);

            DialogBoxes.VinForm vinForm = new DialogBoxes.VinForm();
            vinForm.Vin = currentVin;
            if (vinForm.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            string newVin = vinForm.Vin.Trim().ToUpperInvariant();
            if (newVin.Length != 17)
            {
                MessageBox.Show("The VIN must be 17 characters.", "VIN", MessageBoxButtons.OK);
                return;
            }

            this.AddUserMessage("Unlocking PCM...");
            if (!await commands.Unlock(pcmInfo, CancellationToken.None))
            {
                this.AddUserMessage("Unable to unlock PCM.");
                return;
            }

            byte[] vinBytes = Encoding.ASCII.GetBytes(newVin);
            if (await commands.WriteDataByIdentifier(Gmlan.VinDataIdentifier, vinBytes, CancellationToken.None))
            {
                this.AddUserMessage("VIN successfully updated to " + newVin);
                MessageBox.Show(
                    "VIN updated to " + newVin + " successfully.\n\n" +
                    "Now turn the ignition off and leave the power connected for\n" +
                    "5 seconds to complete the process and save the change to flash.",
                    "Good news.", MessageBoxButtons.OK);
            }
            else
            {
                MessageBox.Show("Unable to change the VIN to " + newVin + ".", "Bad news.", MessageBoxButtons.OK);
            }
        }

        /// <summary>Decode the VIN from a GMLAN 1A 90 response [5A 90 ...]; null if it is not one.</summary>
        private static string? DecodeCanVin(Response<byte[]> response)
        {
            if (response.Status != ResponseStatus.Success)
            {
                return null;
            }
            byte[] bytes = response.Value;
            if (bytes == null || bytes.Length < 3 || bytes[0] != Gmlan.ReadDataByIdentifierResponse || bytes[1] != Gmlan.VinDataIdentifier)
            {
                return null;
            }
            string text = Encoding.ASCII.GetString(bytes.Skip(2).ToArray());
            text = new string(text.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
            if (text.Length > 17)
            {
                text = text.Substring(0, 17);
            }
            return text;
        }

        /// <summary>
        /// Read the entire contents of the flash.
        /// </summary>
        private void readFullContentsButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                this.StartOperationFromDialog(false, WriteType.Full);
            }
        }

        private async void StartOperationFromDialog(bool defaultIsWrite, WriteType defaultWriteType)
        {
            // Probe the bus first so the dialog can offer only the write types this PCM supports and
            // default to calibration (by-segment PCMs) or clone (the rest). Mirrors the Read Properties
            // detect-first flow; on failure the dialog falls back to a manual choice.
            OSIDInfo? detected = null;
            if (this.Vehicle != null)
            {
                try
                {
                    this.DisableUserInput();
                    this.AddUserMessage("Detecting PCM...");
                    DetectedModule? pcm = await this.Vehicle.DetectAndSelectPcm(CancellationToken.None);
                    if (pcm != null)
                    {
                        detected = new OSIDInfo(pcm.Osid);
                        this.AddUserMessage(string.Format(
                            "Detected {0} on {1}: {2}", detected.HardwareType, pcm.Bus, detected.Description));
                    }
                    else
                    {
                        this.AddUserMessage("No PCM detected. Choose the options manually.");
                    }
                }
                catch (Exception ex)
                {
                    this.AddUserMessage("Could not detect the PCM. Choose the options manually. " + ex.Message);
                }
                finally
                {
                    this.EnableUserInput();
                }
            }

            using (OperationSelectionDialogBox dialog = new OperationSelectionDialogBox(defaultIsWrite, defaultWriteType, detected))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK || dialog.Selection == null)
                {
                    return;
                }

                OperationSelection selection = dialog.Selection!;

                if (selection.IsWrite)
                {
                    if (!ConfirmBeforeWrite(this.GetWriteConfirmationText(selection.WriteType)))
                    {
                        return;
                    }

                    BackgroundWorker = new System.Threading.Thread(
                        () => write_BackgroundThread(selection.WriteType, null, selection.UseAutoPcmType, selection.SelectedPcmType));
                }
                else
                {
                    BackgroundWorker = new System.Threading.Thread(
                        () => readFullContents_BackgroundThread(selection.UseAutoPcmType, selection.SelectedPcmType));
                }

                BackgroundWorker.IsBackground = true;
                BackgroundWorker.Start();
            }
        }

        private string GetWriteConfirmationText(WriteType writeType)
        {
            switch (writeType)
            {
                case WriteType.Parameters:
                    return "This will update the parameter block on your PCM.";

                case WriteType.OsPlusCalibrationPlusBoot:
                    return "This will replace the operating system and calibration on your PCM.";

                case WriteType.Calibration:
                    return "This will replace the calibration on your PCM.";

                case WriteType.Full:
                    return "This will replace the contents of the flash memory on your PCM.";

                default:
                    return "This will update your PCM.";
            }
        }

        /// <summary>
        /// Prompt the user before a write operation.
        /// </summary>
        /// <param name="description">The first line of text in the dialog box.</param>
        /// <returns>True if the user wants to proceed, false if not.</returns>
        private bool ConfirmBeforeWrite(string description)
        {
            DialogResult result;
            if (Configuration.Settings.ConnectionVerified)
            {
                result = MessageBox.Show(
                    description,
                    MainForm.ClickOkToContinue,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);
            }
            else
            {
                result = MessageBox.Show(
                    string.Format(
                        MainForm.UnverifiedConnectionWarning,
                        description),
                    MainForm.UnverifiedConnectionWarningTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1); ;
            }

            switch(result)
            {
                case DialogResult.OK:
                case DialogResult.Yes:
                    return true;

                case DialogResult.No:
                    this.AddUserMessage(MainForm.WiseChoice);
                    return false;

                case DialogResult.Cancel:
                default:
                    return false;
            }
        }

        /// <summary>
        /// Write Calibration.
        /// </summary>
        private void writeCalibrationButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                this.StartOperationFromDialog(true, WriteType.Calibration);
            }
        }

        /// <summary>
        /// Write the parameter blocks (VIN, problem history, etc)
        /// </summary>
        private void writeParametersButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                this.StartOperationFromDialog(true, WriteType.Parameters);
            }
        }

        /// <summary>
        /// Write Os, Calibration and Boot.
        /// </summary>
        private void writeOSCalibrationBootToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                this.StartOperationFromDialog(true, WriteType.OsPlusCalibrationPlusBoot);
            }
        }

        /// <summary>
        /// Write Full flash (Clone)
        /// </summary>
        private void writeFullToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                this.StartOperationFromDialog(true, WriteType.Full);
            }
        }

        /// <summary>
        /// Compare block CRCs of a file and the PCM.
        /// </summary>
        private void quickComparisonButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                BackgroundWorker = new System.Threading.Thread(() => write_BackgroundThread(WriteType.Compare));
                BackgroundWorker.IsBackground = true;
                BackgroundWorker.Start();
            }
        }

        private void testWriteButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                BackgroundWorker = new System.Threading.Thread(() => write_BackgroundThread(WriteType.TestWrite));
                BackgroundWorker.IsBackground = true;
                BackgroundWorker.Start();
            }
        }

        /// <summary>
        /// Test something in a kernel.
        /// </summary>
        private void testKernelButton_Click(object sender, EventArgs e)
        {
            if (!BackgroundWorker.IsAlive)
            {
                BackgroundWorker = new System.Threading.Thread(() => exitKernel_BackgroundThread());
                BackgroundWorker.IsBackground = true;
                BackgroundWorker.Start();
            }
        }

        /// <summary>
        /// Set the cancelOperation flag, so that an ongoing operation can be aborted.
        /// </summary>
        private void CancelButton_Click(object sender, EventArgs e)
        {
            if ((this.currentWriteType != WriteType.None) && (this.currentWriteType != WriteType.TestWrite))
            {
                var choice = MessageBox.Show(
                    this,
                    "Canceling now could make your PCM unusable." + Environment.NewLine +
                    "Are you sure you want to take that risk?",
                    "PCM Hammer",
                    MessageBoxButtons.YesNo);

                if (choice == DialogResult.No)
                {
                    return;
                }
            }

            this.AddUserMessage("Cancel button clicked.");
            this.cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Wrapper for the base class's Invoke method
        /// </summary>
        /// <remarks>
        /// This returns a Task for compatibility with ReadManager, which needs
        /// a Task-returning method due to a quirk of the Uno Platform code
        /// generator.
        /// </remarks>
        private Task InvokeWrapper(Action action)
        {
            this.Invoke(action);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Read the entire contents of the flash.
        /// </summary>
        private async void readFullContents_BackgroundThread(bool useAutoPcmType = true, PcmType selectedPcmType = PcmType.Undefined)
        {
            using (new AwayMode())
            {
                try
                {
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.DisableUserInput();
                        this.cancelButton.Enabled = true;
                    });

                    if (this.Vehicle == null)
                    {
                        // This shouldn't be possible - it would mean the buttons
                        // were enabled when they shouldn't be.
                        return;
                    }

                    // Get the path to save the image to.
                    string? path = null;
                    await this.InvokeWrapper(async () => path = await this.PromptForFileSavePath());

                    if (path == null)
                    {
                        this.AddUserMessage("Read canceled.");
                        return;
                    }

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    this.cancellationTokenSource = new CancellationTokenSource();
                    ReadManager readManager = new ReadManager(
                        this,
                        this.Vehicle,
                        (action) => { this.Invoke(action); return Task.CompletedTask; },
                        this.PromptForFileSavePath,
                        this.PromptForPcmType,
                        this.Alert,
                        this.PromptForYesNo,
                        this.cancellationTokenSource.Token);

                    if (await readManager.Read(path, forcedPcmType))
                    {
                        // This will suppress the scary warnings prior to writing.
                        Configuration.Settings.ConnectionVerified = true;
                    }
                }
                catch (Exception exception)
                {
                    this.AddUserMessage("Read failed: " + exception.ToString());
                }
                finally
                {
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.EnableUserInput();
                        this.cancelButton.Enabled = false;
                    });

                    // The token / token-source can only be cancelled once, so we need to make sure they won't be re-used.
                    this.cancellationTokenSource = null;
                }
            }
        }

        private Task<string?> PromptForFileSavePath()
        {
            string? path = this.ShowSaveAsDialog();

            if (path == null)
            {
                return Task.FromResult<string?>(null);
            }

            this.AddUserMessage("Will save to " + path);

            return Task.FromResult<string?>(path);
        }

        private Task<PcmType> PromptForPcmType()
        {
            PcmTypeSelectorDialogBox pcmTypeDialog = new PcmTypeSelectorDialogBox();
            DialogResult dialogResult = pcmTypeDialog.ShowDialog();
            if (dialogResult == DialogResult.OK)
            {
                return Task.FromResult(pcmTypeDialog.SelectedPcmType);
            }
            else
            {
                return Task.FromResult(PcmType.Undefined);
            }
        }

        private Task<bool> PromptForYesNo(string message, string title)
        {
            DialogResult dialogResult = MessageBox.Show(message, title, MessageBoxButtons.YesNo);
            return Task.FromResult(dialogResult == DialogResult.Yes);
        }

        private Task Alert(string message, string title)
        {
            MessageBox.Show(message, title);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Write changes to the PCM's flash memory.
        /// </summary>
        private async void write_BackgroundThread(WriteType writeType, string? path = null, bool useAutoPcmType = true, PcmType selectedPcmType = PcmType.Undefined)
        {
            using (new AwayMode())
            {
                try
                {
                    this.currentWriteType = writeType;

                    if (this.Vehicle == null)
                    {
                        // This shouldn't be possible - it would mean the buttons 
                        // were enabled when they shouldn't be.
                        return;
                    }

                    this.cancellationTokenSource = new CancellationTokenSource();
                    
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.DisableUserInput();
                        this.cancelButton.Enabled = true;

                        if (string.IsNullOrWhiteSpace(path))
                        {
                            path = this.ShowOpenDialog();
                        }
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            return;
                        }
                    });

                    if (path == null)
                    {
                        this.AddUserMessage(
                            writeType == WriteType.TestWrite ?
                                "Test write canceled." :
                                "Write canceled.");
                        return;
                    }

                    this.AddUserMessage(path);

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    WriteManager writer = new WriteManager(
                        this,
                        this.Vehicle,
                        writeType,
                        this.Alert,
                        this.PromptForYesNo,
                        this.cancellationTokenSource.Token);

                    bool success = await writer.Write(path, forcedPcmType);

                    if (success)
                    {
                    // This will suppress the scary warnings prior to writing.
                    Configuration.Settings.ConnectionVerified = true;
                }
                }
                catch (IOException exception)
                {
                    this.AddUserMessage(exception.ToString());
                }
                finally
                {
                    this.currentWriteType = WriteType.None;

                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.EnableUserInput();
                        this.cancelButton.Enabled = false;
                    });

                    // The token / token-source can only be cancelled once, so we need to make sure they won't be re-used.
                    this.cancellationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// From the user's perspective, this is for exiting the kernel, in 
        /// case it remains running after an aborted operation.
        /// 
        /// From the developer's perspective, this is for testing, debugging,
        /// and investigating kernel features that are development.
        /// </summary>
        private async void exitKernel_BackgroundThread()
        {
            try
            {
                if (this.Vehicle == null)
                {
                    // This shouldn't be possible - it would mean the buttons 
                    // were enabled when they shouldn't be.
                    return;
                }

                this.Invoke((MethodInvoker)delegate ()
                {
                    this.DisableUserInput();
                    this.cancelButton.Enabled = true;
                });

                this.cancellationTokenSource = new CancellationTokenSource();

                try
                {
                    await this.Vehicle.ExitKernel(true, false, this.cancellationTokenSource.Token, null);
                }
                catch (IOException exception)
                {
                    this.AddUserMessage(exception.ToString());
                }
            }
            finally
            {
                this.Invoke((MethodInvoker)delegate ()
                {
                    this.EnableUserInput();
                    this.cancelButton.Enabled = false;
                });

                // The token / token-source can only be cancelled once, so we need to make sure they won't be re-used.
                this.cancellationTokenSource = null;
            }
        }

        private async void testFileChecksumsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string? path = this.ShowOpenDialog();
            if (path == null)
            {
                return;
            }

            this.AddUserMessage("Examining " + path);

            byte[] image;
            try
            {
                using (Stream stream = File.OpenRead(path))
                {
                    image = new byte[stream.Length];
                    int bytesRead = await stream.ReadAsync(image, 0, (int)stream.Length);
                    if (bytesRead != stream.Length)
                    {
                        // If this happens too much, we should try looping rather than reading the whole file in one shot.
                        this.AddUserMessage("Unable to load file.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                this.AddUserMessage($"Unable to open file: {ex.Message}");
                return;
            }

            // Sanity checks. 
            FileValidator validator = new FileValidator(image, this);
            if (validator.IsValid())
            {
                this.AddUserMessage("File is " + new OSIDInfo(validator.GetFileType()).Description + ".");
                this.AddUserMessage("All checksums are valid.");
            }
            else
            {
                this.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
            }
        }
    }
}

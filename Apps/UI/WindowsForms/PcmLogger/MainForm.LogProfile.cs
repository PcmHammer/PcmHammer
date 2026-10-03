// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;


namespace PcmHacking
{
    public partial class MainForm
    {
        private LogProfile currentProfile = new LogProfile();
        private string? currentProfilePath = null;
        private bool currentProfileIsDirty = false;
        private bool historyLoadedFromFile;

        private const string FileFilter = "Log Profiles (*.LogProfile)|*.LogProfile|All Files|*.*";

        /// <summary>
        /// Generate a file name for the current log file.
        /// </summary>
        /// <summary>
        /// Open a recorded log and put it on the monitors for inspection.
        /// </summary>
        /// <remarks>
        /// Loaded into the same <see cref="LogHistory"/> the live monitors read from, so the existing
        /// traces and cursor work on it with no drawing code of their own. Only available while
        /// stopped: live samples would append to the same history and walk over what was loaded.
        /// </remarks>
        private void LoadLogForInspection()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Open log";
                dialog.Filter = "Log files (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.InitialDirectory = Configuration.Settings.LogDirectory;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    LoggedData data;
                    using (StreamReader reader = new StreamReader(dialog.FileName))
                    {
                        data = LogFileReader.Read(reader);
                    }

                    this.ShowLoadedLog(dialog.FileName, data);
                }
                catch (Exception exception)
                {
                    this.AddUserMessage($"Unable to open {dialog.FileName}: {exception.Message}");
                    this.AddDebugMessage(exception.ToString());
                    this.recordingStatus.Text = "Unable to open that log.";
                }
            }
        }

        /// <summary>
        /// The id a loaded column should carry: the definition's own id when the heading names a PID
        /// this database knows, otherwise the heading itself.
        /// </summary>
        private string ColumnIdFor(LoggedColumn column)
        {
            const string PidPrefix = "PID ";

            if (this.database != null &&
                column.Address != null &&
                column.Address.StartsWith(PidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string pid = column.Address.Substring(PidPrefix.Length).Trim();
                if (this.database.TryGetParameter(pid, out PidParameter parameter) && parameter != null)
                {
                    return parameter.Id;
                }
            }

            return column.Heading;
        }

        private void ShowLoadedLog(string path, LoggedData data)
        {
            if (data.Samples.Count == 0)
            {
                this.recordingStatus.Text = "That log has no rows.";
                return;
            }

            // Keyed by parameter id where the column names one, so the gauges and the monitors bind
            // to a loaded log exactly as they do to a live one. A column this database has no
            // definition for - a log from another car, or a parameter since renamed - falls back to
            // its heading, which still draws but binds to nothing.
            List<string> ids = data.Columns.Select(this.ColumnIdFor).ToList();
            LogHistory loaded = new LogHistory(ids);

            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                names[ids[i]] = data.Columns[i].Name;
                units[ids[i]] = data.Columns[i].Units;
            }

            this.loggedParameters = names;
            this.loggedUnits = units;

            for (int row = 0; row < data.Samples.Count; row++)
            {
                loaded.Append(data.Timestamps[row], data.Samples[row]);
            }

            this.SetMonitorHistory(loaded);
            this.historyLoadedFromFile = true;
            this.UpdateClearLogButton();
            this.UpdateMonitorTimeBox();
            this.UpdateTitle();

            string malformed = data.MalformedRows > 0
                ? $", {data.MalformedRows:N0} unreadable rows skipped"
                : string.Empty;

            this.recordingStatus.Text =
                $"{Path.GetFileName(path)}: {data.Samples.Count:N0} frames, " +
                $"{data.Columns.Count:N0} columns{malformed}";

            this.AddUserMessage(this.recordingStatus.Text);
        }

        /// <summary>
        /// Offer to put the recording somewhere of the user's choosing.
        /// </summary>
        /// <remarks>
        /// The log is written as it is recorded, not buffered and saved at the end, so a crash or a
        /// flat battery costs nothing. That is worth keeping, so this moves the finished file rather
        /// than being the thing that writes it - and cancelling leaves the recording where it is
        /// instead of throwing it away.
        /// </remarks>
        private bool PromptToSaveLog(string recordedPath, int frames)
        {
            if (!File.Exists(recordedPath))
            {
                return true;
            }

            if (frames == 0)
            {
                // Nothing was captured, so there is nothing to ask about.
                try
                {
                    File.Delete(recordedPath);
                }
                catch (Exception exception)
                {
                    this.AddDebugMessage("Unable to remove the empty log file: " + exception.Message);
                }

                this.recordingStatus.Text = "Nothing was recorded.";
                return true;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = $"Save log ({frames:N0} frames)";
                dialog.Filter = "Log files (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.DefaultExt = "csv";
                dialog.InitialDirectory = Path.GetDirectoryName(recordedPath);
                dialog.FileName = Path.GetFileName(recordedPath);
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    // Cancelled. The recording stays where it was written, and Save stays available.
                    return false;
                }

                if (string.Equals(dialog.FileName, recordedPath, StringComparison.OrdinalIgnoreCase))
                {
                    this.recordingStatus.Text = $"Recorded {frames:N0} frames to {recordedPath}";
                    return true;
                }

                try
                {
                    if (File.Exists(dialog.FileName))
                    {
                        File.Delete(dialog.FileName);
                    }

                    File.Move(recordedPath, dialog.FileName);
                    this.recordingStatus.Text = $"Recorded {frames:N0} frames to {dialog.FileName}";
                    return true;
                }
                catch (Exception exception)
                {
                    // The recording still exists where it was written, which is what matters.
                    this.AddUserMessage(
                        $"Unable to save to {dialog.FileName}: {exception.Message}. " +
                        $"The recording is still at {recordedPath}.");
                    this.recordingStatus.Text = $"Recorded {frames:N0} frames to {recordedPath}";
                    return false;
                }
            }
        }

        private string GenerateLogFilePath()
        {
            string baseName = DateTime.Now.ToString("yyyyMMdd_HHmm") + "_" + this.fileName;
            string candidate = Path.Combine(Configuration.Settings.LogDirectory, baseName + ".csv");

            // A second recording in the same minute must never truncate the earlier file.
            for (int suffix = 1; File.Exists(candidate); suffix++)
            {
                candidate = Path.Combine(
                    Configuration.Settings.LogDirectory,
                    baseName + "_" + suffix.ToString("D2") + ".csv");
            }

            return candidate;
        }

        private void SetDirtyFlag(bool newValue)
        {
            this.currentProfileIsDirty = newValue;
            this.UpdateTitle();
        }

        private void SetFileName(string fileName)
        {
            this.fileName = fileName;
            this.UpdateTitle();
        }

        /// <summary>
        /// The window title: the app, the open profile, what it is doing, and whether the profile
        /// has unsaved changes.
        /// </summary>
        /// <remarks>
        /// Composed from the current state rather than edited in place. Appending and stripping a
        /// "*" meant the title depended on what had happened to it before, and there is now a status
        /// in there that changes independently of the profile.
        ///
        /// Playback against the other two is the distinction worth making: all three fill the same
        /// gauges and plots, and only the title says whether the numbers are coming from the car.
        /// </remarks>
        private void UpdateTitle()
        {
            StringBuilder title = new StringBuilder(AppName);

            if (this.currentProfilePath != null)
            {
                title.Append(" - ").Append(this.fileName);
            }

            // Recording before Logging: writing to a file is also reading the car, and the stronger
            // claim is the one worth showing.
            if (this.saving)
            {
                title.Append(" - Recording");
            }
            else if (this.viewing)
            {
                title.Append(" - Logging");
            }
            else if (this.history != null && this.history.Count > 0)
            {
                // Disconnected with samples still on screen. That is playback whether they came from
                // a file or from the recording just stopped - stopping leaves them there on purpose,
                // so there is time to look at what was captured.
                title.Append(" - Playback");
            }

            if (this.currentProfileIsDirty)
            {
                title.Append('*');
            }

            this.Text = title.ToString();
        }


        private void newButton_Click(object sender, EventArgs e)
        {
            if (this.currentProfileIsDirty)
            {
                if (this.SaveIfNecessary() == DialogResult.Cancel)
                {
                    return;
                }
            }

            this.SetFileName(DefaultFileName);
            this.currentProfilePath = null;

            this.SetDirtyFlag(false);

            this.ResetProfile();
            this.UpdateGridFromProfile();
        }

        private void openButton_Click(object sender, EventArgs e)
        {
            if (this.currentProfileIsDirty)
            {
                if (this.SaveIfNecessary() == DialogResult.Cancel)
                {
                    return;
                }
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = FileFilter;
            dialog.Multiselect = false;
            dialog.Title = "Open Log Profile";
            dialog.ValidateNames = true;

            DialogResult result = dialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                this.OpenProfile(dialog.FileName);
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (currentProfilePath == null)
            {
                saveAsButton_Click(sender, e);
                return;
            }

            LogProfileWriter.Write(this.currentProfile, this.currentProfilePath);
            this.SetDirtyFlag(false);
        }

        private void saveAsButton_Click(object sender, EventArgs e)
        {
            this.ShowSaveAs();
        }

        private DialogResult SaveIfNecessary()
        {
            DialogResult result = MessageBox.Show(
                this,
                "Would you like to save the current profile before continuing?",
                "The current profile has changed.",
                MessageBoxButtons.YesNoCancel);

            switch (result)
            {
                case DialogResult.Yes:
                    if (string.IsNullOrEmpty(currentProfilePath))
                    {
                        if (this.ShowSaveAs() == DialogResult.Cancel)
                        {
                            return DialogResult.Cancel;
                        }

                        return DialogResult.OK;
                    }
                    else
                    {
                        this.saveButton_Click(this, new EventArgs());
                        return DialogResult.OK;
                    }

                case DialogResult.Cancel:
                    return DialogResult.Cancel;

                default: // DialogResult.No
                    return DialogResult.OK;
            }
        }

        private DialogResult ShowSaveAs()
        { 
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = FileFilter;
            dialog.OverwritePrompt = true;
            dialog.AddExtension = true;
            DialogResult result = dialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                this.currentProfilePath = dialog.FileName;
                this.SetFileName(Path.GetFileNameWithoutExtension(this.currentProfilePath));

                try
                {
                    LogProfileWriter.Write(this.currentProfile, this.currentProfilePath);
                    this.SetDirtyFlag(false);
                }
                catch (Exception exception)
                {
                    this.AddDebugMessage(exception.ToString());
                    this.AddUserMessage(exception.Message);
                }

                foreach(PathDisplayAdapter adapter in this.profileList.Items)
                {
                    if (adapter.Path == this.currentProfilePath)
                    {
                        this.profileList.Items.Remove(adapter);
                        break;
                    }
                }

                this.profileList.Items.Insert(0, new PathDisplayAdapter(this.currentProfilePath));
            }

            return result;
        }

        private void ResetProfile()
        {
            this.currentProfile = new LogProfile();
        }

        private void OpenProfile(string path)
        {
            bool alreadyInList = false;
            foreach (PathDisplayAdapter adapter in this.profileList.Items)
            {
                if (adapter.Path == path)
                {
                    alreadyInList = true;
                    this.profileList.SelectedItem = adapter;
                    break;
                }
            }

            if (!alreadyInList)
            {
                PathDisplayAdapter newAdapter = new PathDisplayAdapter(path);
                this.profileList.Items.Insert(0, newAdapter);
                this.profileList.SelectedItem = newAdapter;
            }

            this.currentProfilePath = path;
            this.SetFileName(Path.GetFileNameWithoutExtension(this.currentProfilePath));

            LogProfileReader reader = new LogProfileReader(this.database, this.osid, this);
            this.currentProfile = reader.Read(this.currentProfilePath);
            Configuration.Settings.LastProfile = this.currentProfilePath;
            this.UpdateGridFromProfile();
            this.SetDirtyFlag(false);
        }
    }
}

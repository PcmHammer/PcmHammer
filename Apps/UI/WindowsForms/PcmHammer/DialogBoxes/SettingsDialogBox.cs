// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking.DialogBoxes
{
    public partial class SettingsDialogBox : Form
    {
        private readonly ILogger logger;

        public SettingsDialogBox(ILogger logger)
        {
            this.logger = logger;
            InitializeComponent();
        }

        private void SettingsDialogBox_Load(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Configuration.Settings.LogDirectory))
            {
                Configuration.Settings.LogDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                Configuration.Settings.Save();
            }
            logDirectoryTextBox.Text = Configuration.Settings.LogDirectory;

            if (!string.IsNullOrWhiteSpace(Configuration.Settings.BinDirectory))
            {
                binDirectoryTextBox.Text = Configuration.Settings.BinDirectory;
            }

            saveUserLogOnExitCheckBox.Checked = Configuration.Settings.SaveUserLogOnExit;
            saveDebugLogOnExitCheckBox.Checked = Configuration.Settings.SaveDebugLogOnExit;
            mainWindowPersistenceCheckBox.Checked = Configuration.Settings.MainWindowPersistence;
            useLogSaveAsDialogCheckBox.Checked = Configuration.Settings.UseLogSaveAsDialog;

            // Cross flashing and force-write-all are runtime-only flags: they are never saved and
            // always start cleared on launch. Reflect their current in-memory values without enabling Apply.
            allowCrossFlashingCheckBox.Checked = RuntimeSettings.AllowCrossFlashing;
            forceWriteAllSectorsCheckBox.Checked = RuntimeSettings.ForceWriteAllSectors;
            allowModuleImportCheckBox.Checked = RuntimeSettings.AllowModuleImport;
            applyButton.Enabled = false;
        }

        private void SaveSettings()
        {
            if (Configuration.Settings.LogDirectory != logDirectoryTextBox.Text)
            {
                Configuration.Settings.LogDirectory = logDirectoryTextBox.Text;
            }

            if (Configuration.Settings.SaveUserLogOnExit != saveUserLogOnExitCheckBox.Checked)
            {
                Configuration.Settings.SaveUserLogOnExit = saveUserLogOnExitCheckBox.Checked;
            }

            if (Configuration.Settings.SaveDebugLogOnExit != saveDebugLogOnExitCheckBox.Checked)
            {
                Configuration.Settings.SaveDebugLogOnExit = saveDebugLogOnExitCheckBox.Checked;
            }

            if (Configuration.Settings.MainWindowPersistence != mainWindowPersistenceCheckBox.Checked)
            {
                Configuration.Settings.MainWindowPersistence = mainWindowPersistenceCheckBox.Checked;
            }

            if (Configuration.Settings.BinDirectory != binDirectoryTextBox.Text)
            {
                Configuration.Settings.BinDirectory = binDirectoryTextBox.Text;
            }

            if (Configuration.Settings.UseLogSaveAsDialog != useLogSaveAsDialogCheckBox.Checked)
            {
                Configuration.Settings.UseLogSaveAsDialog = useLogSaveAsDialogCheckBox.Checked;
            }

            Configuration.Settings.Save();
            applyButton.Enabled = false;
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            if(applyButton.Enabled)
            {
                SaveSettings();
            }
            DialogResult = DialogResult.OK;
        }

        private void applyButton_Click(object sender, EventArgs e)
        {
            SaveSettings();
            applyButton.Enabled = false;
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void logDirectoryTextBox_TextChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void logDirectoryButton_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Configuration.Settings.LogDirectory;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    logDirectoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void saveUserLogOnExitCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void saveDebugLogOnExitCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void mainWindowPersistenceCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void binDirectoryTextBox_TextChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void binDirectoryButton_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Configuration.Settings.BinDirectory;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    binDirectoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void useLogSaveAsDialogCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            applyButton.Enabled = true;
        }

        private void allowCrossFlashingCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // This flag is runtime-only and applies immediately, so it deliberately does NOT
            // enable Apply or get written by SaveSettings. It resets to cleared on next launch.
            if (allowCrossFlashingCheckBox.Checked)
            {
                DialogResult choice = MessageBox.Show(
                    this,
                    "This setting disables cross flash protection. It allows you to brick your pcm with an incompatible file. For advanced recovery and developer use only. Continue?",
                    "Brick Risk",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (choice != DialogResult.Yes)
                {
                    // Revert; this re-enters and falls through to set the flag false.
                    allowCrossFlashingCheckBox.Checked = false;
                    return;
                }

                this.logger.AddUserMessage("##########################################################");
                this.logger.AddUserMessage("# User accepts risk and disabled cross flash protection. #");
                this.logger.AddUserMessage("#         Incompatible files can now be written.         #");
                this.logger.AddUserMessage("#       You can now brick your PCM with this tool.       #");
                this.logger.AddUserMessage("#      Exit the program to restore normal operation.     #");
                this.logger.AddUserMessage("##########################################################");
            }

            RuntimeSettings.AllowCrossFlashing = allowCrossFlashingCheckBox.Checked;
        }

        private void forceWriteAllSectorsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Runtime-only flag, applies immediately and resets to cleared on next launch, so it
            // deliberately does NOT enable Apply or get written by SaveSettings.
            RuntimeSettings.ForceWriteAllSectors = forceWriteAllSectorsCheckBox.Checked;

            if (forceWriteAllSectorsCheckBox.Checked)
            {
                this.logger.AddUserMessage("Force write all flash sectors enabled.");
            }
        }

        private void allowModuleImportCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Runtime-only flag, applies immediately and resets to cleared on next launch, so it
            // deliberately does NOT enable Apply or get written by SaveSettings. It reveals the
            // File -> Import Bin item, the power tool for building a file with a specific slave module.
            RuntimeSettings.AllowModuleImport = allowModuleImportCheckBox.Checked;

            if (allowModuleImportCheckBox.Checked)
            {
                this.logger.AddUserMessage("Module import enabled (File -> Import Bin is now available).");
            }
        }
    }
}

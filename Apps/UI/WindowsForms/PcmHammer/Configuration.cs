// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Configuration;
using System.IO;
using System.Windows.Forms;

namespace PcmHacking
{
    public class Configuration
    {
        private static PcmHammer.Properties.Settings? settings;

        public static PcmHammer.Properties.Settings Settings
        {
            get
            {
                if (settings == null)
                {
                    settings = LoadSettings();
                }
                return settings;
            }
        }

        private static PcmHammer.Properties.Settings LoadSettings()
        {
            var s = PcmHammer.Properties.Settings.Default;
            try
            {
                // Probe a property to force lazy deserialization from disk now,
                // so corruption is caught here rather than mid-operation.
                _ = s.MainWindowPersistence;
                return s;
            }
            catch (ConfigurationErrorsException ex)
            {
                TryDeleteCorruptConfig(ex);
                try
                {
                    // The singleton is in a broken state; create a fresh instance
                    // so the app continues with defaults. It can save on close,
                    // creating a clean file for the next launch.
                    return new PcmHammer.Properties.Settings();
                }
                catch
                {
                    MessageBox.Show(
                        "The application configuration is corrupt and could not be recovered.\n\nThe application will now close.",
                        "Configuration Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Environment.Exit(1);
                    return null!; // unreachable: Environment.Exit terminates the process
                }
            }
        }

        private static void TryDeleteCorruptConfig(ConfigurationErrorsException ex)
        {
            try
            {
                string filename = ex.Filename;
                if (string.IsNullOrEmpty(filename) && ex.InnerException is ConfigurationErrorsException inner)
                {
                    filename = inner.Filename;
                }
                if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
                {
                    File.Delete(filename);
                }
            }
            catch { }
        }
    }
}

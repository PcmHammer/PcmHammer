// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Configuration;

namespace PcmHacking
{
    /// <summary>
    /// Saves application settings without letting a configuration problem take the app down.
    /// </summary>
    /// <remarks>
    /// Settings live in two files - the section declarations in the exe's own config, the values in
    /// the per-user config - and a save throws if either is missing or unreadable. On the way out of
    /// FormClosing that is an unhandled exception rather than a lost preference.
    ///
    /// Deliberately does not try to repair anything. The failure looks the same whichever file is at
    /// fault, and the remedy for one (discard the values) destroys the user's settings to work around
    /// a problem with the other.
    /// </remarks>
    public static class SettingsStore
    {
        public static bool TrySave(ApplicationSettingsBase settings, out string? message)
        {
            try
            {
                settings.Save();
                message = null;
                return true;
            }
            catch (Exception exception)
            {
                message = "Settings could not be saved: " + exception.Message;
                return false;
            }
        }
    }
}

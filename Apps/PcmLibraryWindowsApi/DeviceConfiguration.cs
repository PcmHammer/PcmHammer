// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class DeviceConfiguration
    {
#if !LINUX_CLI
        // Backed by the WinForms application settings (System.Configuration), which is
        // Windows-only. The Linux CLI never reads persisted settings - it takes the
        // device from the command line - so this is excluded from that build.
        public static PcmLibraryWindowsForms.Properties.Settings Settings = PcmLibraryWindowsForms.Properties.Settings.Default;

        /// <summary>
        /// Save, reporting rather than throwing. Prefer this to Settings.Save().
        /// </summary>
        public static void Save(ILogger? logger = null)
        {
            SettingsStore.TrySave(Settings, out string? message);

            if (message != null)
            {
                logger?.AddUserMessage(message);
            }
        }
#endif

        /// <summary>
        /// The category names, kept here for the WinForms code that has always used this path. The
        /// values live in <see cref="DeviceConstants"/>, which every front end can reach; having had
        /// them written out twice is how two copies of the same list came to exist.
        /// </summary>
        public class Constants
        {
            public const string DeviceCategorySerial = DeviceConstants.DeviceCategorySerial;
            public const string DeviceCategoryJ2534 = DeviceConstants.DeviceCategoryJ2534;
            public const string DeviceCategoryBT = DeviceConstants.DeviceCategoryBT;
            public const string DeviceCategoryNone = DeviceConstants.DeviceCategoryNone;
        }

        /// <summary>Whether a category names an interface, as opposed to meaning "none chosen".</summary>
        public static bool IsDeviceSelected(string? category) =>
            DeviceConstants.IsDeviceSelected(category);
    }
}

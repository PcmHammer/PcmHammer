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
#endif

        public class Constants
        {
            public const string DeviceCategorySerial = "Serial";
            public const string DeviceCategoryJ2534 = "J2534";
            public const string DeviceCategoryBT = "Bluetooth";
        }
    }
}

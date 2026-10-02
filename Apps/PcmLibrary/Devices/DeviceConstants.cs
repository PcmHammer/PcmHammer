// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public class DeviceConstants
    {
        public const string DeviceCategorySerial = "Serial";
        public const string DeviceCategoryJ2534 = "J2534";
        public const string DeviceCategoryBT = "Bluetooth";

        /// <summary>
        /// No interface chosen. A category rather than a special case, so an empty slot is an
        /// ordinary value everywhere - saved, compared and offered like any other.
        /// </summary>
        /// <remarks>
        /// Also what an unset or unrecognised category means, so first run and a device that has
        /// since disappeared both land somewhere sensible instead of silently adopting whichever
        /// interface happened to enumerate first.
        /// </remarks>
        public const string DeviceCategoryNone = "None";

        /// <summary>Whether a category names an interface, as opposed to meaning "none chosen".</summary>
        public static bool IsDeviceSelected(string? category)
        {
            return category == DeviceCategorySerial
                || category == DeviceCategoryJ2534
                || category == DeviceCategoryBT;
        }
    }
}

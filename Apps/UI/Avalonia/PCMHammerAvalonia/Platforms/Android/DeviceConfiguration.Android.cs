// SPDX-License-Identifier: GPL-3.0-only
// The Android stand-in for the Windows DeviceConfiguration (which lives in PcmLibraryWindowsApi and is
// not referenced on Android). The shared device picker only needs the category constants, which simply
// forward to the cross-platform DeviceConstants in PcmLibrary. The Windows version also persists the
// selected device to user settings; on Android there is no such store, so that surface is omitted.
#if ANDROID
namespace PcmHacking;

public static class DeviceConfiguration
{
    public static class Constants
    {
        public const string DeviceCategorySerial = DeviceConstants.DeviceCategorySerial;
        public const string DeviceCategoryJ2534 = DeviceConstants.DeviceCategoryJ2534;
        public const string DeviceCategoryBT = DeviceConstants.DeviceCategoryBT;
        public const string DeviceCategoryNone = DeviceConstants.DeviceCategoryNone;
    }
}
#endif

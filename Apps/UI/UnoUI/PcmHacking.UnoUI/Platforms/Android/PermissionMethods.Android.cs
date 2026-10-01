// SPDX-License-Identifier: GPL-3.0-only
using Android;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking.UnoUI.Platforms.Android
{
    public class PermissionMethods
    {
        public static async Task RequestAndroidPermissions()
        {
            // POST_NOTIFICATIONS is API 33 and FOREGROUND_SERVICE_DATA_SYNC is API 34; the floor is 31,
            // so both are gated. OperatingSystem.IsAndroidVersionAtLeast is used rather than an SdkInt
            // comparison because it is the form the platform-compatibility analyzer recognises.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                await Request(Manifest.Permission.PostNotifications);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                await Request(Manifest.Permission.ForegroundServiceDataSync);
            }

            await Request(Manifest.Permission.Bluetooth);
            await Request(Manifest.Permission.BluetoothAdvertise);
            await Request(Manifest.Permission.BluetoothScan);
            await Request(Manifest.Permission.BluetoothConnect);
            await Request(Manifest.Permission.AccessCoarseLocation);
            await Request(Manifest.Permission.Internet);
            await Request(Manifest.Permission.ManageExternalStorage);
            await Request(Manifest.Permission.ReadExternalStorage);
            await Request(Manifest.Permission.WriteExternalStorage);
            await Request(Manifest.Permission.AccessBackgroundLocation);
            await Request(Manifest.Permission.AccessFineLocation);
            await Request(Manifest.Permission.LocationHardware);
            await Request(Manifest.Permission.ForegroundService);
            await GrantStoragePermissions();
        }

        /// <summary>
        /// Ask for one permission. Swallows failures so that one unavailable permission cannot abort
        /// the rest of the chain - storage is requested last, and losing it leaves no kernels on disk.
        /// </summary>
        private static async Task Request(string permission)
        {
            try
            {
                await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, permission);
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>How long to wait for the user to return from the settings screen.</summary>
        private static readonly TimeSpan ReturnFromSettingsTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Whether we already hold the storage access the kernel folder needs.</summary>
        public static async Task<bool> IsStorageGranted()
        {
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                return global::Android.OS.Environment.IsExternalStorageManager;
            }

            return await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage)
                && await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage);
        }

        /// <summary>
        /// Ask for storage access and wait for the answer. On R and later this means a round-trip to a
        /// settings screen, so the result is re-checked afterwards rather than assumed; the old code
        /// fired the request and returned immediately, which left the first run without permission.
        /// </summary>
        public static async Task<bool> GrantStoragePermissions()
        {
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                if (global::Android.OS.Environment.IsExternalStorageManager)
                {
                    return true;
                }

                await Droid.MainActivity.RequestFilePermisions();

                // Wait for the user to come back from the settings screen. Bounded so that leaving the
                // app in the background does not poll forever.
                DateTime deadline = DateTime.UtcNow + ReturnFromSettingsTimeout;
                while (Droid.MainActivity.IsPaused && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100);
                }

                return global::Android.OS.Environment.IsExternalStorageManager;
            }

            return await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage)
                && await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage);
        }

        // The Link metadata in the csproj is preserved verbatim under assets/, hence the doubled name.
        private const string KernelAssetFolder = "Assets/Kernels";

        /// <summary>
        /// Vehicle's base path on Android: the user-visible PCMHammer\Bins folder. Rooted at the
        /// external-storage path the OS reports, which is /storage/emulated/&lt;user&gt; - not always
        /// user 0, on a device with a work profile or a second user.
        /// </summary>
        public static string KernelDirectory
        {
            get
            {
                string root = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
                    ?? "/storage/emulated/0";
                return Path.Combine(root, "PCMHammer", "Bins");
            }
        }

        /// <summary>
        /// Copy every embedded kernel and boot library to the folder the library loads them from.
        /// The list comes from the APK's assets so it cannot drift from what was built. False when
        /// storage access was refused, which leaves the kernels unavailable.
        /// </summary>
        public static async Task<bool> ExtractKernelsToFileAndroid()
        {
            // Wait for the answer rather than firing the request and returning, or the first run
            // extracts nothing and every operation then fails to find its kernel.
            if (!await GrantStoragePermissions())
            {
                return false;
            }

            global::Android.Content.Res.AssetManager? assets =
                global::Android.App.Application.Context.Assets;
            string[] kernelNames = assets?.List(KernelAssetFolder) ?? [];

            Directory.CreateDirectory(KernelDirectory);
            foreach (string kernel in kernelNames)
            {
                if (!kernel.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A file already there wins, so a hand-pushed kernel can be tested without a rebuild.
                string filePath = Path.Combine(KernelDirectory, kernel);
                if (File.Exists(filePath))
                {
                    continue;
                }

                using (Stream asset = assets!.Open($"{KernelAssetFolder}/{kernel}"))
                using (FileStream destination = File.Create(filePath))
                {
                    await asset.CopyToAsync(destination);
                }
            }

            return true;
        }

    }
}
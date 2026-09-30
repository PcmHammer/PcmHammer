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
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.PostNotifications);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.Bluetooth);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothAdvertise);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothScan);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothConnect);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessCoarseLocation);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.Internet);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ManageExternalStorage);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessBackgroundLocation);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessFineLocation);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.LocationHardware);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ForegroundService);
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ForegroundServiceDataSync);
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                bool result = global::Android.OS.Environment.IsExternalStorageManager;
                if (!result)
                {
                    Droid.MainActivity.RequestFilePermisions();
                }
            }
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
        /// The list comes from the APK's assets so it cannot drift from what was built.
        /// </summary>
        public static async Task ExtractKernelsToFileAndroid()
        {
            bool result = global::Android.OS.Environment.IsExternalStorageManager;
            if (!result)
            {
                Droid.MainActivity.RequestFilePermisions();
                return;
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
        }

    }
}
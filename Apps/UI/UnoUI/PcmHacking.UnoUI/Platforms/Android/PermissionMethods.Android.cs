using Android;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking.UnoUI.Platforms.Android
{
    public class PermissionMethods
    {
        public static async Task<bool> IsBluetoothGranted()
        {
            if (await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.Bluetooth) &&
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.BluetoothAdvertise) &&
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.BluetoothScan) &&
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.BluetoothConnect))
            {
                return true;
            }
            return false;
        }

        public async static Task<bool> GrantBluetoothPermissions()
        {
            if (await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.Bluetooth) &&
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothAdvertise) &&
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothScan) &&
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothConnect))
            {
                return true;
            }
            return false;
        }

        public static async Task<bool> IsStorageGranted()
        {
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                if (global::Android.OS.Environment.IsExternalStorageManager)
                {
                    return true;
                }
            }
            else if (await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.ManageExternalStorage) &&
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage) &&
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage))
            {
                return true;
            }
            return false;
        }

        public static async Task<bool> GrantStoragePermissions()
        {
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                bool result = global::Android.OS.Environment.IsExternalStorageManager;
                if (!result)
                {
                    await MainActivity.RequestFilePermisions();
                    while (MainActivity.IsPaused) // TODO: This needs a suitable exit strategy in the event the request does not actually fire.
                    {
                        await Task.Delay(10);
                    }
                    result = global::Android.OS.Environment.IsExternalStorageManager;
                }
                return result;
            }
            else
            {
                if (await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ManageExternalStorage) &&
                    await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage) &&
                    await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage))
                {
                    return true;
                }
            }
            return false;
        }

        public static async Task<bool> IsNotificationsGranted() =>
            await Windows.Extensions.PermissionsHelper.CheckPermission(CancellationToken.None, Manifest.Permission.PostNotifications);


        public static async Task<bool> GrantNotificationPermission() =>
            await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.PostNotifications);

        // TODO: Loop through StoredECU array for all Kernel/Loader names?
        public static async Task ExtractKernelsToFileAndroid()
        {
            if (!await IsStorageGranted())
            {
                return;
            }

                string[] KernelNames = ["Kernel-BlackBox.bin", "Kernel-P01.bin", "Kernel-P04.bin", "Kernel-P04_Early.bin", "Kernel-P05.bin", "Kernel-P08.bin", "Kernel-P10.bin", "Kernel-P11.bin", "Kernel-P12.bin", "Kernel-E54.bin", "Loader-P04.bin"];
                foreach (string kernel in KernelNames)
                {
                    string directory = "/storage/emulated/0/PCMHammer/Bins";
                    string filePath = $"{directory}/{kernel}";
                    Directory.CreateDirectory(directory);
                    if (!File.Exists(filePath))
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/Kernels/{kernel}"));
                        var content = await file.OpenReadAsync();
                        Directory.CreateDirectory(directory);
                        File.WriteAllBytes(filePath, content.AsStream().ToMemoryStream().ToArray());
                    }
                }
            
        }
    }
}
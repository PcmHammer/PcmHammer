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
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
            {
                bool result = global::Android.OS.Environment.IsExternalStorageManager;
                if (!result)
                {
                    Droid.MainActivity.RequestFilePermisions();
                }
            }
        }

        public static async Task ExtractKernelsToFileAndroid()
        {
            bool result = global::Android.OS.Environment.IsExternalStorageManager;
            if (!result)
            {
                Droid.MainActivity.RequestFilePermisions();
                return;
            }
            string[] KernelNames = ["Kernel-BlackBox.bin", "Kernel-P01.bin", "Kernel-P04.bin", "Kernel-P04_Early.bin", "Kernel-P05.bin", "Kernel-P08.bin", "Kernel-P10.bin", "Kernel-P11.bin", "Kernel-P12.bin", "Kernel-E54.bin", "Loader-P04.bin"];
            foreach (string kernel in KernelNames)
            {
                string directory = "/storage/emulated/0/PCMHammer/Bins";
                string filePath = $"{directory}/{kernel}";
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
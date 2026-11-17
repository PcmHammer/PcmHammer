using System;
using System.Collections.Generic;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace PCMHammerAvalonia.Android;

[Activity(
    Label = "PCM Hammer",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        this.RequestBluetoothPermissions();
    }

    // Bluetooth needs runtime permission: SCAN/CONNECT on API 31+, otherwise location (required to
    // enumerate Bluetooth devices on older Android). Prompting here is enough; the device picker lists
    // paired adapters once granted.
    private void RequestBluetoothPermissions()
    {
        List<string> permissions = new();
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            permissions.Add(global::Android.Manifest.Permission.BluetoothConnect);
            permissions.Add(global::Android.Manifest.Permission.BluetoothScan);
        }
        else
        {
            permissions.Add(global::Android.Manifest.Permission.AccessFineLocation);
        }

        try
        {
            this.RequestPermissions(permissions.ToArray(), 100);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("RequestBluetoothPermissions failed: " + exception);
        }
    }
}

using Android;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Provider;
using AndroidX.Core.App;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace PcmHacking.UnoUI.Droid;
[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    protected async override void OnCreate(Bundle bundle)
    {
        base.OnCreate(bundle);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.Bluetooth);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothAdvertise);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothScan);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.BluetoothConnect);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessBackgroundLocation);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.LocationHardware);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessCoarseLocation);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.AccessFineLocation);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.Internet);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ManageExternalStorage);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.ReadExternalStorage);
        await Windows.Extensions.PermissionsHelper.TryGetPermission(CancellationToken.None, Manifest.Permission.WriteExternalStorage);
        //RequestFilePermisions();
    }

    public static void RequestFilePermisions()
    {
        try
        {
            global::Android.Net.Uri uri = global::Android.Net.Uri.Parse("package:" + Current.PackageName);
            Intent intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission, uri);
            Current.StartActivity(intent);
        }
        catch (Exception ex)
        {
            Intent intent = new Intent();
            intent.SetAction(Settings.ActionManageAppAllFilesAccessPermission);
            Current.StartActivity(intent);
        }
    }
}

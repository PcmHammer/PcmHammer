// SPDX-License-Identifier: GPL-3.0-only
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
            Current?.StartActivity(intent);
        }
    }
}

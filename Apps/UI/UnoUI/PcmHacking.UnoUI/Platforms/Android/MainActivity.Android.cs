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

namespace PcmHacking.UnoUI.Platforms.Android;
[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    public static bool IsPaused { get; private set; }
    protected async override void OnCreate(Bundle bundle)
    {
        base.OnCreate(bundle);
    }

    protected override void OnResume()
    {
        IsPaused = false;
        base.OnResume();
    }

    protected override void OnPause()
    {
        IsPaused = true;
        base.OnPause();
    }

    public static async Task RequestFilePermisions()
    {
        try
        {
            global::Android.Net.Uri? uri = global::Android.Net.Uri.Parse("package:" + Current.PackageName);
            Intent intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission, uri);
            Current.StartActivityForResult(intent, 1);
            while (!IsPaused) // TODO: This needs a suitable exit strategy in the event the request does not actually fire.
            {
                await Task.Delay(1);
            }
        }
        catch (System.Exception ex)
        {
            Intent intent = new Intent();
            intent.SetAction(Settings.ActionManageAppAllFilesAccessPermission);
            Current?.StartActivity(intent);
        }
    }
}

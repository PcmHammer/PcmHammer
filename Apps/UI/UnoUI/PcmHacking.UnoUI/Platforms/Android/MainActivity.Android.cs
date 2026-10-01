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
    /// <summary>
    /// True while the settings screen is in front of us. The all-files permission is granted in a
    /// separate activity, so this is how the caller knows the round-trip has started and finished.
    /// </summary>
    public static bool IsPaused { get; private set; }

    /// <summary>How long to wait for the permission screen to appear before giving up on it.</summary>
    private static readonly TimeSpan PermissionScreenTimeout = TimeSpan.FromSeconds(10);

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

    /// <summary>
    /// Open the all-files-access settings screen and return once it is actually in front of the user.
    /// Gives up after <see cref="PermissionScreenTimeout"/> so a request that never fires cannot hang
    /// the caller forever.
    /// </summary>
    public static async Task RequestFilePermisions()
    {
        try
        {
            global::Android.Net.Uri? uri = global::Android.Net.Uri.Parse("package:" + Current.PackageName);
            Intent intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission, uri);
            Current.StartActivity(intent);

            DateTime deadline = DateTime.UtcNow + PermissionScreenTimeout;
            while (!IsPaused && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
        catch (System.Exception)
        {
            Intent intent = new Intent();
            intent.SetAction(Settings.ActionManageAppAllFilesAccessPermission);
            Current?.StartActivity(intent);
        }
    }
}

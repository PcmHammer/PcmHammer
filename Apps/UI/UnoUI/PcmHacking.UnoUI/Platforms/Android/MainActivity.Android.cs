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
    /// Release the connection when the app is actually going away.
    /// </summary>
    /// <remarks>
    /// Android promises no callback at all when it kills the process, so this cannot be made
    /// complete - the OS closes the socket itself in that case. This covers what it does tell us
    /// about: the user finishing the app, or swiping it away. IsFinishing distinguishes that from a
    /// destroy-and-recreate; configuration changes do not reach here anyway, because the activity
    /// declares ConfigurationChanges = AllConfigChanges and Uno handles them in place.
    /// </remarks>
    protected override void OnDestroy()
    {
        if (this.IsFinishing)
        {
            try
            {
                App.ApplicationShutdownSource.Cancel();

                // Not awaited: OnDestroy has to return promptly or Android reports an ANR. The
                // shutdown takes the connection semaphore, so an operation still running is left
                // alone rather than cut off part-way.
                _ = App.GetService<Services.IConnectionService>().AwaitConnectionShutdown();
            }
            catch (System.Exception)
            {
                // Teardown is best effort; the process is going away regardless.
            }
        }

        base.OnDestroy();
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

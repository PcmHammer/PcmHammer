#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

// Crowbar: Shout-out to this Github repo for a few key insights on how to implement proper notifications: https://github.com/putridparrot/blog-projects

namespace PcmHacking.UnoUI.Platforms.Android
{
    [Service(Exported = true, Name = "net.PcmHacking.Uno.DataService")]
    public class DataService : Service {
        private static string _actionType = string.Empty;
        private static string NOTIFICATION_CHANNEL_ID = "pcmhacking";
        private static int NOTIFICATION_ID = 1;
        private static string NOTIFICATION_CHANNEL_NAME = "notification";
        public static Context? _currentContext { get; private set; }
        private static Task? _runnerTask;
        private static Action? _onSuccessAction;
        private static Action? _onFailureAction;

        public static void StartService(string actionType, Task tasktoRun, Action onSuccess, Action onFailure) {
            _actionType = actionType;
            _runnerTask = tasktoRun;
            _onSuccessAction = onSuccess;
            _onFailureAction = onFailure;
            Intent intent = new Intent(global::Android.App.Application.Context, typeof(DataService));
            intent.SetAction("START");
            global::Android.App.Application.Context.StartForegroundService(intent);
        }

        public static void StopService() {
            _actionType = string.Empty;
            Intent intent = new Intent(global::Android.App.Application.Context, typeof(DataService));
            intent.SetAction("STOP");
            global::Android.App.Application.Context.StartForegroundService(intent);
        }

        public static void UpdateProgress(int progress, string details) {
            System.Diagnostics.Debug.WriteLine($"Calling Android label progress {progress}");
            var notifcationManager = global::Android.App.Application.Context.GetSystemService(Context.NotificationService) as NotificationManager;
            NotificationCompat.Builder note = BuildNotification(_actionType, progress, details);
            notifcationManager.Notify(NOTIFICATION_ID, note.Build());
        }

        private static NotificationCompat.Builder? BuildNotification(string actionType, int progress, string details)
        {
            if(_currentContext == null) {
                return null;
            }
            var notification = new NotificationCompat.Builder(_currentContext, NOTIFICATION_CHANNEL_ID);
            notification.SetAutoCancel(false);
            notification.SetOngoing(true);
            notification.SetOnlyAlertOnce(true);
            notification.SetPriority(1);
            notification.SetProgress(100, progress, false);
            notification.SetSmallIcon(Resource.Mipmap.icon);
            notification.SetContentTitle("PCM Hammer Operation running");
            notification.SetContentText($"The requested {actionType} opperation is in progress: {details}");
            return notification;
        }

        public static bool IsServiceRunning()
        {
            ActivityManager manager = (ActivityManager)global::Android.App.Application.Context.GetSystemService(ActivityService);
            foreach (var service in manager.GetRunningServices(int.MaxValue))
            {
                if (service.Service.ShortClassName.Contains(nameof(DataService)))
                {
                    return true;
                }
            }
            return false;
        }

        private void StartForegroundService() {
            _currentContext = this;
            var notifcationManager = GetSystemService(Context.NotificationService) as NotificationManager;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O) {
                CreateNotificationChannel(notifcationManager);
            }
            var noteBuilder = BuildNotification(_actionType, 0, string.Empty);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake) {
                StartForeground(NOTIFICATION_ID, noteBuilder.Build(), global::Android.Content.PM.ForegroundService.TypeDataSync);
            } else {
                StartForeground(NOTIFICATION_ID, noteBuilder.Build());
            }
        }

        private void CreateNotificationChannel(NotificationManager notificationMnaManager) {
            var channel = new NotificationChannel(NOTIFICATION_CHANNEL_ID, NOTIFICATION_CHANNEL_NAME,
            NotificationImportance.High);
            notificationMnaManager.CreateNotificationChannel(channel);
        }

        public override IBinder OnBind(Intent intent) {
            return null;
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId) {
            if (intent == null) {
                return StartCommandResult.RedeliverIntent;
            }
            if (intent.Action == "START") {
                StartForegroundService();
                if(_runnerTask != null) {
                    _runnerTask.ContinueWith((t) => {
                        StopForeground(StopForegroundFlags.Remove);
                        StopSelfResult(startId);
                        if (t.IsCompletedSuccessfully && _onSuccessAction != null) {
                            _onSuccessAction?.Invoke();
                        } else if (t.IsFaulted && _onFailureAction != null) {
                            _onFailureAction?.Invoke();
                        }
                    });
                }
                return StartCommandResult.Sticky;
            } else if (intent.Action == "STOP") {
                StopForeground(StopForegroundFlags.Remove);
                StopSelfResult(startId);
            }
            return StartCommandResult.Sticky;
        }
    }
}
#endif
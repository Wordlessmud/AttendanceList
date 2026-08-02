using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Platforms.Android;

public sealed class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [(global::Android.Manifest.Permission.PostNotifications, true)]
            : [];
}

public sealed class AndroidLocalNotificationScheduler : ILocalNotificationScheduler
{
    internal const string ChannelId = "event-reminders";
    private const string StoredIdsKey = "scheduled_event_reminder_ids_android";

    public async Task<bool> EnsurePermissionAsync(bool requestPermission)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }
        var status = await Permissions.CheckStatusAsync<NotificationPermission>();
        if (status != PermissionStatus.Granted && requestPermission)
        {
            status = await Permissions.RequestAsync<NotificationPermission>();
        }
        return status == PermissionStatus.Granted;
    }

    public Task ReplaceAsync(IReadOnlyList<ReminderOccurrence> occurrences)
    {
        var context = global::Android.App.Application.Context;
        EnsureNotificationChannel(context);
        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarmManager is null)
        {
            return Task.CompletedTask;
        }
        var previous = JsonSerializer.Deserialize<List<int>>(
            Preferences.Default.Get(StoredIdsKey, "[]")) ?? [];
        foreach (var requestCode in previous)
        {
            var pending = PendingIntent.GetBroadcast(
                context,
                requestCode,
                new Intent(context, typeof(ReminderAlarmReceiver)),
                SafeFlags(PendingIntentFlags.NoCreate));
            if (pending is not null)
            {
                alarmManager.Cancel(pending);
                pending.Cancel();
            }
        }
        var ids = new List<int>();
        foreach (var occurrence in occurrences)
        {
            var requestCode = StableId(occurrence.Id);
            var intent = new Intent(context, typeof(ReminderAlarmReceiver));
            intent.PutExtra("notification_id", requestCode);
            intent.PutExtra("title", occurrence.Title);
            intent.PutExtra("message", occurrence.Message);
            var pending = PendingIntent.GetBroadcast(
                context,
                requestCode,
                intent,
                SafeFlags(PendingIntentFlags.UpdateCurrent));
            if (pending is null)
            {
                continue;
            }
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                alarmManager.SetAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    occurrence.When.ToUnixTimeMilliseconds(),
                    pending);
            }
            else
            {
                alarmManager.Set(
                    AlarmType.RtcWakeup,
                    occurrence.When.ToUnixTimeMilliseconds(),
                    pending);
            }
            ids.Add(requestCode);
        }
        Preferences.Default.Set(StoredIdsKey, JsonSerializer.Serialize(ids));
        return Task.CompletedTask;
    }

    private static int StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return (BitConverter.ToInt32(hash, 0) & int.MaxValue) is 0 ? 1 : BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }

    internal static PendingIntentFlags SafeFlags(PendingIntentFlags flags) =>
        OperatingSystem.IsAndroidVersionAtLeast(23)
            ? flags | PendingIntentFlags.Immutable
            : flags;

    internal static void EnsureNotificationChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var nativeManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        nativeManager?.CreateNotificationChannel(new NotificationChannel(
            ChannelId,
            LocalizationService.T("ReminderChannelName"),
            NotificationImportance.Default));
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }
        var manager = NotificationManagerCompat.From(context);
        if (manager is null)
        {
            return;
        }
        AndroidLocalNotificationScheduler.EnsureNotificationChannel(context);
        var openApp = new Intent(context, typeof(MainActivity));
        openApp.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var contentIntent = PendingIntent.GetActivity(
            context,
            0,
            openApp,
            AndroidLocalNotificationScheduler.SafeFlags(PendingIntentFlags.UpdateCurrent));
        var builder = new NotificationCompat.Builder(context, AndroidLocalNotificationScheduler.ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_stat_attendance);
        builder.SetContentTitle(intent.GetStringExtra("title") ?? LocalizationService.T("AppTitle"));
        builder.SetContentText(intent.GetStringExtra("message") ?? string.Empty);
        builder.SetAutoCancel(true);
        if (contentIntent is not null)
        {
            builder.SetContentIntent(contentIntent);
        }
        var notification = builder.Build();
        if (notification is not null)
        {
            manager.Notify(intent.GetIntExtra("notification_id", 1), notification);
        }
        AndroidReminderRescheduler.Begin(this, context);
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[]
{
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    Intent.ActionTimeChanged,
    Intent.ActionTimezoneChanged
})]
public sealed class ReminderRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            AndroidReminderRescheduler.Begin(this, context);
        }
    }
}

internal static class AndroidReminderRescheduler
{
    public static void Begin(BroadcastReceiver receiver, Context context)
    {
        var pendingResult = receiver.GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                if (context.ApplicationContext is Microsoft.Maui.IPlatformApplication platformApplication
                    && platformApplication.Services.GetService(typeof(ReminderCoordinator)) is ReminderCoordinator coordinator)
                {
                    await coordinator.RescheduleAsync(requestPermission: false);
                }
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}

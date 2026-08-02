using System.Security;
using AttendanceList.Models;
using AttendanceList.Services;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AttendanceList.Platforms.Windows;

public sealed class WindowsLocalNotificationScheduler : ILocalNotificationScheduler
{
    private const string Group = "AttendList";

    public Task<bool> EnsurePermissionAsync(bool requestPermission)
    {
        var notifier = CreateNotifier();
        return Task.FromResult(notifier.Setting == NotificationSetting.Enabled);
    }

    public Task ReplaceAsync(IReadOnlyList<ReminderOccurrence> occurrences)
    {
        var notifier = CreateNotifier();
        if (notifier.Setting != NotificationSetting.Enabled)
        {
            throw new InvalidOperationException(LocalizationService.T("NotificationsDisabledMessage"));
        }

        foreach (var existing in notifier.GetScheduledToastNotifications().Where(t => t.Group == Group).ToList())
        {
            notifier.RemoveFromSchedule(existing);
        }

        foreach (var occurrence in occurrences)
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast scenario=\"reminder\"><visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(occurrence.Title)}</text>" +
                $"<text>{SecurityElement.Escape(occurrence.Message)}</text>" +
                "</binding></visual>" +
                "<audio src=\"ms-winsoundevent:Notification.Reminder\" loop=\"false\"/>" +
                "</toast>");
            var toast = new ScheduledToastNotification(xml, occurrence.When)
            {
                Group = Group,
                Tag = ShortTag(occurrence.Id)
            };
            notifier.AddToSchedule(toast);
        }

        var scheduledCount = notifier.GetScheduledToastNotifications().Count(value => value.Group == Group);
        if (scheduledCount != occurrences.Count)
        {
            throw new InvalidOperationException(LocalizationService.T("WindowsNotificationVerificationFailed"));
        }
        return Task.CompletedTask;
    }

    private static ToastNotifier CreateNotifier()
    {
        if (!HasPackageIdentity())
        {
            throw new InvalidOperationException(LocalizationService.T("WindowsNotificationSetupRequired"));
        }

        try
        {
            return ToastNotificationManager.CreateToastNotifier();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                LocalizationService.T("WindowsNotificationSetupRequired"),
                exception);
        }
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(global::Windows.ApplicationModel.Package.Current.Id.Name);
        }
        catch
        {
            return false;
        }
    }

    private static string ShortTag(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16];
    }
}

using AttendanceList.Models;

namespace AttendanceList.Services;

public interface ILocalNotificationScheduler
{
    Task<bool> EnsurePermissionAsync(bool requestPermission);
    Task ReplaceAsync(IReadOnlyList<ReminderOccurrence> occurrences);
}

public sealed class ReminderCoordinator(
    DatabaseService database,
    ILocalNotificationScheduler scheduler)
{
    public string? LastErrorMessage { get; private set; }

    public async Task<bool> SaveAndRescheduleAsync(
        EventReminder reminder,
        bool requestPermission)
    {
        await database.SaveEventReminderAsync(reminder);
        if (await RescheduleAsync(requestPermission))
        {
            return true;
        }

        var schedulingError = LastErrorMessage;
        if (reminder.IsEnabled)
        {
            // Preserve the user's configuration, but never leave a reminder
            // looking active when the operating system rejected its schedule.
            reminder.IsEnabled = false;
            await database.SaveEventReminderAsync(reminder);
            await RescheduleAsync(requestPermission: false);
        }
        LastErrorMessage = schedulingError;
        return false;
    }

    public async Task<bool> RescheduleAsync(bool requestPermission)
    {
        try
        {
            LastErrorMessage = null;
            if (!await scheduler.EnsurePermissionAsync(requestPermission))
            {
                return false;
            }
            var reminders = (await database.GetEventRemindersAsync())
                .Where(r => r.IsEnabled)
                .ToList();
            var occurrences = new List<ReminderOccurrence>();
            var today = DateTime.Today;
            var horizon = today.AddDays(45);
            foreach (var reminder in reminders)
            {
                if (!await database.IsReminderContextActiveAsync(reminder))
                {
                    continue;
                }
                var classGroup = await database.GetClassAsync(reminder.ClassGroupId);
                var definition = await database.GetEventAsync(reminder.EventDefinitionId);
                var overrides = (await database.GetDayOverridesAsync(reminder.ClassGroupId, today, horizon))
                    .ToDictionary(value => value.DateKey);
                var days = (await database.GetClassDaysAsync(reminder.ClassGroupId, today, horizon))
                    .ToDictionary(value => value.DateKey);
                var completions = await database.GetEventCompletionsAsync(days.Values.Select(value => value.Id));
                var completedDayIds = completions
                    .Where(value => value.EventDefinitionId == reminder.EventDefinitionId)
                    .Select(value => value.ClassDayId)
                    .ToHashSet();
                for (var date = today; date <= horizon; date = date.AddDays(1))
                {
                    var dateKey = DatabaseService.DateKey(date);
                    overrides.TryGetValue(dateKey, out var dayOverride);
                    var operating = dayOverride?.Kind switch
                    {
                        DayOverrideKind.Excluded => false,
                        DayOverrideKind.Included or DayOverrideKind.Special => true,
                        _ => DatabaseService.HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
                    };
                    days.TryGetValue(dateKey, out var day);
                    var completed = day is not null && completedDayIds.Contains(day.Id);
                    if (!ReminderRules.ShouldSchedule(
                            date,
                            reminder.DaysMask,
                            reminder.StartDateKey,
                            reminder.EndDateKey,
                            reminder.OnlyOperatingDays,
                            operating,
                            reminder.OnlyIfIncomplete,
                            completed))
                    {
                        continue;
                    }
                    var whenLocal = date.AddMinutes(reminder.TimeMinutes);
                    var when = new DateTimeOffset(whenLocal);
                    if (when <= DateTimeOffset.Now)
                    {
                        continue;
                    }
                    occurrences.Add(new ReminderOccurrence(
                        $"r{reminder.Id}-{date:yyyyMMdd}",
                        when,
                        $"{classGroup.Name} · {definition.Name}",
                        string.IsNullOrWhiteSpace(reminder.Message)
                            ? LocalizationService.T("ReminderDefaultMessage")
                            : reminder.Message,
                        reminder.ClassGroupId,
                        reminder.EventDefinitionId,
                        dateKey));
                }
            }
            await scheduler.ReplaceAsync(occurrences);
            return true;
        }
        catch (Exception exception)
        {
            LastErrorMessage = exception.Message;
            System.Diagnostics.Debug.WriteLine(exception);
            return false;
        }
    }
}

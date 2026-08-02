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
    private const long MaximumDiagnosticLogLength = 512 * 1024;
    private readonly SemaphoreSlim _rescheduleGate = new(1, 1);

    public string? LastErrorMessage { get; private set; }

    public async Task<bool> SaveAndRescheduleAsync(
        EventReminder reminder,
        bool requestPermission)
    {
        await _rescheduleGate.WaitAsync();
        try
        {
            await database.SaveEventReminderAsync(reminder);
            if (await RescheduleCoreAsync(requestPermission))
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
                await RescheduleCoreAsync(requestPermission: false);
            }
            LastErrorMessage = schedulingError;
            return false;
        }
        finally
        {
            _rescheduleGate.Release();
        }
    }

    public async Task<bool> RescheduleAsync(bool requestPermission)
    {
        await _rescheduleGate.WaitAsync();
        try
        {
            return await RescheduleCoreAsync(requestPermission);
        }
        finally
        {
            _rescheduleGate.Release();
        }
    }

    private async Task<bool> RescheduleCoreAsync(bool requestPermission)
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
            LastErrorMessage = DescribeException(exception);
            await TryWriteDiagnosticLogAsync(exception);
            System.Diagnostics.Debug.WriteLine(exception);
            return false;
        }
    }

    private static string DescribeException(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message, StringComparer.Ordinal))
            {
                messages.Add(current.Message.Trim());
            }
        }

        var message = messages.Count == 0
            ? "The operating system returned no error message."
            : string.Join(" -> ", messages);
        return $"{message} [{exception.GetType().FullName}; HRESULT 0x{exception.HResult:X8}]";
    }

    private static async Task TryWriteDiagnosticLogAsync(Exception exception)
    {
        try
        {
            var directory = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "notification-errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaximumDiagnosticLogLength)
            {
                File.Delete(path);
            }

            await File.AppendAllTextAsync(
                path,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never turn a notification failure into an app failure.
        }
    }
}

namespace AttendanceList.Services;

public static class ReminderRules
{
    public static bool ShouldSchedule(
        DateTime date,
        int daysMask,
        string startDateKey,
        string? endDateKey,
        bool onlyOperatingDays,
        bool isOperating,
        bool onlyIfIncomplete,
        bool isCompleted)
    {
        var key = date.Date.ToString("yyyy-MM-dd");
        return ScheduleRules.HasDay(daysMask, date.DayOfWeek)
            && key.CompareTo(startDateKey) >= 0
            && (string.IsNullOrWhiteSpace(endDateKey) || key.CompareTo(endDateKey) <= 0)
            && (!onlyOperatingDays || isOperating)
            && (!onlyIfIncomplete || !isCompleted);
    }
}

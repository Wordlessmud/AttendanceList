namespace AttendanceList.Services;

public static class ScheduleRules
{
    public static bool HasDay(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;
}

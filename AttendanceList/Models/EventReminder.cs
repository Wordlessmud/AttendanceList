using SQLite;

namespace AttendanceList.Models;

[Table("EventReminders")]
public sealed class EventReminder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [Indexed]
    public int EventDefinitionId { get; set; }

    public int TimeMinutes { get; set; } = 9 * 60;
    public int DaysMask { get; set; } = 127;
    public string StartDateKey { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string? EndDateKey { get; set; }
    public bool OnlyOperatingDays { get; set; } = true;
    public bool OnlyIfIncomplete { get; set; } = true;
    public bool IsEnabled { get; set; } = true;

    [MaxLength(240)]
    public string Message { get; set; } = string.Empty;
}

public sealed record ReminderOccurrence(
    string Id,
    DateTimeOffset When,
    string Title,
    string Message,
    int ClassGroupId,
    int EventDefinitionId,
    string DateKey);

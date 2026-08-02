using SQLite;

namespace AttendanceList.Models;

[Table("EventRecords")]
public sealed class EventRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDayPersonId { get; set; }

    [Indexed]
    public int EventDefinitionId { get; set; }

    // Zero means deliberately left unmarked; it is not a user-defined status.
    public int StatusOptionId { get; set; }

    [MaxLength(2000)]
    public string Note { get; set; } = string.Empty;

    public string RecordedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
    public string UpdatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

[Table("EventCompletions")]
public sealed class EventCompletion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDayId { get; set; }

    [Indexed]
    public int EventDefinitionId { get; set; }

    public string CompletedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

public sealed class DailyEventItem
{
    public required ClassDayPerson DayPerson { get; init; }
    public required EventRecord Record { get; init; }
    public string Name => DayPerson.DisplayNameSnapshot;
    public bool IsExpected => DayPerson.IsExpected;
    public string Note => Record.Note;
    public int StatusOptionId => Record.StatusOptionId;
}

namespace AttendanceList.Models;

public sealed class DailyTrackingItem
{
    public required ClassDayPerson DayPerson { get; init; }
    public required TrackingRecord Record { get; init; }
    public string Name => DayPerson.DisplayNameSnapshot;
    public bool IsExpected => DayPerson.IsExpected;
    public string Note => DayPerson.Note;
    public int Status => Record.Status;
}

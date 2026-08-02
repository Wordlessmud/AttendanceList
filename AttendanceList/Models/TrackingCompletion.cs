using SQLite;

namespace AttendanceList.Models;

[Table("TrackingCompletions")]
public sealed class TrackingCompletion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDayId { get; set; }

    public TrackingKind Kind { get; set; }
    public string CompletedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

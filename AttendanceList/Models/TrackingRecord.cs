using SQLite;

namespace AttendanceList.Models;

[Table("TrackingRecords")]
public sealed class TrackingRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDayPersonId { get; set; }

    public TrackingKind Kind { get; set; }
    public int Status { get; set; }
    public string RecordedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
    public string UpdatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

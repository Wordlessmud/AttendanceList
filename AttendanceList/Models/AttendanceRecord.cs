using SQLite;

namespace AttendanceList.Models;

[Table("AttendanceRecords")]
public sealed class AttendanceRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SessionId { get; set; }

    [Indexed]
    public int PersonId { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Unknown;
    public string RecordedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
    public string UpdatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

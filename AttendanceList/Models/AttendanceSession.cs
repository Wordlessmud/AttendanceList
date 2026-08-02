using SQLite;

namespace AttendanceList.Models;

[Table("AttendanceSessions")]
public sealed class AttendanceSession
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique, MaxLength(10)]
    public string SessionDate { get; set; } = string.Empty;

    public string StartedAtIso { get; set; }
        = DateTimeOffset.Now.ToString("O");

    public string? CompletedAtIso { get; set; }

    public string Notes { get; set; } = string.Empty;

    [SQLite.Ignore]
    public bool IsFinalised =>
        !string.IsNullOrWhiteSpace(CompletedAtIso);
}

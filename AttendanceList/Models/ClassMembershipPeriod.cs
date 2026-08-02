using SQLite;

namespace AttendanceList.Models;

[Table("ClassMembershipPeriods")]
public sealed class ClassMembershipPeriod
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassMembershipId { get; set; }

    [MaxLength(10)]
    public string StartDateKey { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");

    [MaxLength(10)]
    public string? EndDateKey { get; set; }
}

using SQLite;

namespace AttendanceList.Models;

[Table("ClassMemberships")]
public sealed class ClassMembership
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [Indexed]
    public int PersonId { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int ExpectedDaysMask { get; set; } = 127;
    public string StartDateKey { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string? EndDateKey { get; set; }
}

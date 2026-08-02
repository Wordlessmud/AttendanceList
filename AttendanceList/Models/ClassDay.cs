using SQLite;

namespace AttendanceList.Models;

[Table("ClassDays")]
public sealed class ClassDay
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [MaxLength(10)]
    public string DateKey { get; set; } = string.Empty;

    [MaxLength(120)]
    public string OrganizationNameSnapshot { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ClassNameSnapshot { get; set; } = string.Empty;

    [MaxLength(80)]
    public string SubjectLabelSnapshot { get; set; } = string.Empty;

    public RosterSource RosterSource { get; set; }
    public bool IsBackfill { get; set; }
    public string? RosterConfirmedAtIso { get; set; }
    public string CreatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

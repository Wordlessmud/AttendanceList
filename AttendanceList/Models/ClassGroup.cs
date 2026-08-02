using SQLite;

namespace AttendanceList.Models;

[Table("ClassGroups")]
public sealed class ClassGroup
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int OrganizationId { get; set; }

    [NotNull, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string SubjectLabel { get; set; } = string.Empty;

    public bool IsArchived { get; set; }

    // Bit 0 is Sunday, bit 1 is Monday, ... bit 6 is Saturday.
    public int OperatingDaysMask { get; set; } = 127;
    public string CreatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

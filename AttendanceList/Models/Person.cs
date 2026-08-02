using SQLite;

namespace AttendanceList.Models;

[Table("People")]
public sealed class Person
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string CreatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
    public string? ArchivedAtIso { get; set; }
}

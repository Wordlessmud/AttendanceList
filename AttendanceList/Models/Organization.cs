using SQLite;

namespace AttendanceList.Models;

[Table("Organizations")]
public sealed class Organization
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public bool IsArchived { get; set; }
    public string CreatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

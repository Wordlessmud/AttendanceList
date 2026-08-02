using SQLite;

namespace AttendanceList.Models;

public enum DayOverrideKind
{
    Excluded = 1,
    Included = 2,
    Special = 3
}

[Table("DayOverrides")]
public sealed class DayOverride
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [MaxLength(10)]
    public string DateKey { get; set; } = string.Empty;

    public DayOverrideKind Kind { get; set; }

    [MaxLength(80)]
    public string Label { get; set; } = string.Empty;
}

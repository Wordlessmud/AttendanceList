using SQLite;

namespace AttendanceList.Models;

[Table("AppMetadata")]
public sealed class AppMetadata
{
    [PrimaryKey, MaxLength(80)]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

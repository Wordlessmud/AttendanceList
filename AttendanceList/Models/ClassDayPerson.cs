using SQLite;

namespace AttendanceList.Models;

[Table("ClassDayPeople")]
public sealed class ClassDayPerson
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDayId { get; set; }

    [Indexed]
    public int PersonId { get; set; }

    [MaxLength(120)]
    public string DisplayNameSnapshot { get; set; } = string.Empty;

    public bool IsExpected { get; set; }
    public int SortOrderSnapshot { get; set; }

    // Retained only to migrate notes created before schema v7. New notes belong
    // to EventRecord so separate events cannot expose one another's notes.
    [MaxLength(2000)]
    public string Note { get; set; } = string.Empty;
}

using SQLite;

namespace AttendanceList.Models;

public enum EventStatusSemantic
{
    Positive = 1,
    Negative = 2,
    Neutral = 3,
    Excused = 4
}

[Table("EventDefinitions")]
public sealed class EventDefinition
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int OrganizationId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(40)]
    public string SystemKey { get; set; } = string.Empty;

    [MaxLength(12)]
    public string PositiveSymbol { get; set; } = "✓";

    public bool UseExpectedRoster { get; set; } = true;
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }
    public string CreatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

[Table("EventStatusOptions")]
public sealed class EventStatusOption
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int EventDefinitionId { get; set; }

    [MaxLength(80)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(12)]
    public string Symbol { get; set; } = string.Empty;

    public EventStatusSemantic Semantic { get; set; }
    public int SortOrder { get; set; }
}

[Table("ClassEvents")]
public sealed class ClassEvent
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [Indexed]
    public int EventDefinitionId { get; set; }

    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class ClassEventDefinition
{
    public required EventDefinition Event { get; init; }
    public ClassEvent? ClassEvent { get; init; }
    public IReadOnlyList<EventStatusOption> StatusOptions { get; init; } = [];
    public bool IsEnabled => ClassEvent?.IsEnabled == true;
    public string Name => Event.Name;
    public override string ToString() => Name;
}

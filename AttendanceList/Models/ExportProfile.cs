using SQLite;

namespace AttendanceList.Models;

[Table("ExportProfiles")]
public sealed class ExportProfile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassGroupId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public string DefinitionJson { get; set; } = string.Empty;
    public string UpdatedAtIso { get; set; } = DateTimeOffset.Now.ToString("O");
}

public sealed class ExportProfileDefinition
{
    public string Name { get; set; } = string.Empty;
    public int EventDefinitionId { get; set; }
    // Retained only so existing v2 saved profiles can be mapped during migration.
    public TrackingKind Kind { get; set; } = TrackingKind.Attendance;
    public bool DatesAsRows { get; set; } = true;
    public string BaseBucketName { get; set; } = "Regular";
    public bool CompletedOnly { get; set; } = true;
    public bool IncludeNotes { get; set; } = true;
    public bool IncludeRawData { get; set; } = true;
    public string PositiveSymbol { get; set; } = "✓";
    public List<ExportBucketDefinition> Buckets { get; set; } = [];
}

public sealed class ExportBucketDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<int> Weekdays { get; set; } = [];
    public List<string> DateKeys { get; set; } = [];
    public List<string> Labels { get; set; } = [];
    public bool ExcludeFromBase { get; set; } = true;
}

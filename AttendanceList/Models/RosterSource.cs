namespace AttendanceList.Models;

public enum RosterSource
{
    Unspecified = 0,
    HistoricalMembership = 1,
    ManualSelection = 2,
    CurrentRosterAssumption = 3,
    LegacySnapshot = 4
}

public sealed class BackfillRosterCandidate
{
    public int MembershipId { get; init; }
    public int PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool WasMemberOnDate { get; init; }
    public bool IsCurrentMember { get; init; }
    public bool IsExpectedOnDate { get; init; }
}

public sealed class BackfillRosterSelection
{
    public int MembershipId { get; init; }
    public string HistoricalOnlyName { get; init; } = string.Empty;
    public bool IsExpected { get; init; }
}

namespace AttendanceList.Models;

public sealed class ClassMember
{
    public required Person Person { get; init; }
    public required ClassMembership Membership { get; init; }
    public string DisplayName => Person.DisplayName;
    public bool IsActive => Membership.IsActive;
}

namespace AttendanceList.Models;

public sealed class AttendanceItem
{
    public required Person Person { get; init; }
    public required AttendanceRecord Record { get; init; }
    public string Name => Person.DisplayName;
    public AttendanceStatus Status => Record.Status;
}

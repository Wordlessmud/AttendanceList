namespace AttendanceList.Models;

public sealed class RangeReport
{
    public int SessionCount { get; init; }
    public AttendanceSummary Overall { get; init; } = new(0, 0, 0, 0);
    public IReadOnlyList<ReportRow> Rows { get; init; } = [];
}

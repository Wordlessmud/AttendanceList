namespace AttendanceList.Models;

public sealed class HistoryRow
{
    public required AttendanceSession Session { get; init; }
    public required AttendanceSummary Summary { get; init; }
    public string DateText { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string StateText { get; init; } = string.Empty;
}

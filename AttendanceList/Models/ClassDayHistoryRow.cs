namespace AttendanceList.Models;

public sealed class ClassDayHistoryRow
{
    public ClassDay? Day { get; init; }
    public DateTime Date { get; init; }
    public bool IsMissing { get; init; }
    public string DateText { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string StateText { get; init; } = string.Empty;
}

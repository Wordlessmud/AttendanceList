namespace AttendanceList.Models;

public sealed class TrackingReportRow
{
    public int PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Positive { get; init; }
    public int Negative { get; init; }
    public int Excused { get; init; }
    public int Unknown { get; init; }
    public double? Rate => Positive + Negative == 0 ? null : (double)Positive / (Positive + Negative);
    public string RateText => Rate is null ? "—" : Rate.Value.ToString("P0");
}

public sealed class CalendarDateInfo
{
    public DateTime Date { get; init; }
    public bool IsOperating { get; init; }
    public DayOverride? Override { get; init; }
    public ClassDay? ClassDay { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsMissing => IsOperating && ClassDay is null;
    public bool IsIncomplete => ClassDay is not null && !IsCompleted;
}

public sealed class TrackingRangeReport
{
    public TrackingKind Kind { get; init; }
    public int CalendarDayCount { get; init; }
    public int IncludedDayCount { get; init; }
    public int CompletedDayCount { get; init; }
    public TrackingSummary Overall { get; init; } = new(0, 0, 0, 0);
    public IReadOnlyList<TrackingReportRow> Rows { get; init; } = [];
    public IReadOnlyList<CalendarDateInfo> Calendar { get; init; } = [];
    public IReadOnlyList<DateTime> MissingDates { get; init; } = [];
    public IReadOnlyList<DateTime> IncompleteDates { get; init; } = [];
}

namespace AttendanceList.Models;

public sealed class EventSummary
{
    public int Positive { get; init; }
    public int Negative { get; init; }
    public int Neutral { get; init; }
    public int Excused { get; init; }
    public int Unknown { get; init; }
    public int Total => Positive + Negative + Neutral + Excused + Unknown;
}

public sealed class EventReportRow
{
    public int PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public EventSummary Summary { get; init; } = new();
    public string RateText => Summary.Total == 0 ? "—" : $"{Summary.Positive * 100d / Summary.Total:0.#}%";
}

public sealed class EventRangeReport
{
    public EventDefinition Event { get; init; } = new();
    public EventSummary Overall { get; init; } = new();
    public IReadOnlyList<EventReportRow> Rows { get; init; } = [];
    public IReadOnlyList<DateTime> MissingDates { get; init; } = [];
    public IReadOnlyList<DateTime> IncompleteDates { get; init; } = [];
    public int CalendarDayCount { get; init; }
    public int CompletedDayCount { get; init; }
}

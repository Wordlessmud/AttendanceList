using AttendanceList.Models;

namespace AttendanceList.Services;

public static class EventRules
{
    public static EventSummary Summarise(IEnumerable<EventStatusSemantic?> semantics)
    {
        var values = semantics.ToList();
        return new EventSummary
        {
            Positive = values.Count(value => value == EventStatusSemantic.Positive),
            Negative = values.Count(value => value == EventStatusSemantic.Negative),
            Neutral = values.Count(value => value == EventStatusSemantic.Neutral),
            Excused = values.Count(value => value == EventStatusSemantic.Excused),
            Unknown = values.Count(value => value is null)
        };
    }
}

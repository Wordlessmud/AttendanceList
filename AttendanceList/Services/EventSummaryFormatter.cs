using AttendanceList.Models;

namespace AttendanceList.Services;

public static class EventSummaryFormatter
{
    public static string Format(
        EventSummary summary,
        IReadOnlyList<EventStatusOption> options,
        Func<string, string>? translate = null)
    {
        translate ??= LocalizationService.T;
        var parts = new List<string>();
        AddGroup(parts, EventStatusSemantic.Positive, summary.Positive, "PositiveGroup", options, translate);
        AddGroup(parts, EventStatusSemantic.Negative, summary.Negative, "NegativeGroup", options, translate);
        AddGroup(parts, EventStatusSemantic.Neutral, summary.Neutral, "NeutralGroup", options, translate);
        AddGroup(parts, EventStatusSemantic.Excused, summary.Excused, "ExcusedGroup", options, translate);
        parts.Add($"{translate("UnmarkedGroup")} {summary.Unknown}");
        return string.Join(" · ", parts);
    }

    private static void AddGroup(
        ICollection<string> parts,
        EventStatusSemantic semantic,
        int count,
        string fallbackKey,
        IReadOnlyList<EventStatusOption> options,
        Func<string, string> translate)
    {
        var matching = options.Where(value => value.Semantic == semantic).ToList();
        if (matching.Count == 0)
        {
            return;
        }
        var label = matching.Count == 1 ? matching[0].Label : translate(fallbackKey);
        parts.Add($"{label} {count}");
    }
}

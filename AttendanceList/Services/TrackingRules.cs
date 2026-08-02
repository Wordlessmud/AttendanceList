using AttendanceList.Models;

namespace AttendanceList.Services;

public static class TrackingRules
{
    public static TrackingSummary Summarise(IEnumerable<int> statuses, TrackingKind kind)
    {
        var list = statuses.ToList();
        return new TrackingSummary(
            list.Count(status => status == 1),
            list.Count(status => status == 2),
            kind == TrackingKind.Attendance ? list.Count(status => status == 3) : 0,
            list.Count(status => status == 0));
    }
}

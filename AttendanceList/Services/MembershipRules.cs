using AttendanceList.Models;

namespace AttendanceList.Services;

public static class MembershipRules
{
    public static bool IncludesDate(
        ClassMembership membership,
        IEnumerable<ClassMembershipPeriod> periods,
        string dateKey)
    {
        var savedPeriods = periods.ToList();
        if (savedPeriods.Count == 0)
        {
            return Includes(dateKey, membership.StartDateKey, membership.EndDateKey);
        }

        return savedPeriods.Any(period => Includes(dateKey, period.StartDateKey, period.EndDateKey));
    }

    private static bool Includes(string dateKey, string startDateKey, string? endDateKey) =>
        string.CompareOrdinal(startDateKey, dateKey) <= 0
        && (string.IsNullOrWhiteSpace(endDateKey) || string.CompareOrdinal(endDateKey, dateKey) >= 0);
}

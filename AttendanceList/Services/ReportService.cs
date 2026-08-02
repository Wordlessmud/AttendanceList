using AttendanceList.Models;

namespace AttendanceList.Services;

public sealed class ReportService(DatabaseService database)
{
    public async Task<EventRangeReport> CreateEventAsync(
        int classGroupId,
        int eventDefinitionId,
        DateTime start,
        DateTime end,
        bool completedOnly = true)
    {
        if (end.Date < start.Date)
        {
            (start, end) = (end, start);
        }
        start = start.Date;
        end = end.Date;
        var definition = await database.GetEventAsync(eventDefinitionId);
        var options = await database.GetEventStatusOptionsAsync(eventDefinitionId);
        var optionById = options.ToDictionary(o => o.Id);
        var classGroup = await database.GetClassAsync(classGroupId);
        var days = await database.GetClassDaysAsync(classGroupId, start, end);
        var daysByDate = days.ToDictionary(d => d.DateKey);
        var overrides = await database.GetDayOverridesAsync(classGroupId, start, end);
        var overridesByDate = overrides.ToDictionary(o => o.DateKey);
        var completions = await database.GetEventCompletionsAsync(days.Select(d => d.Id));
        var completedDayIds = completions
            .Where(c => c.EventDefinitionId == eventDefinitionId)
            .Select(c => c.ClassDayId)
            .ToHashSet();

        var missing = new List<DateTime>();
        var incomplete = new List<DateTime>();
        var operatingDateKeys = new HashSet<string>(StringComparer.Ordinal);
        var operatingDayIds = new HashSet<int>();
        var operatingCount = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var key = DatabaseService.DateKey(date);
            overridesByDate.TryGetValue(key, out var dayOverride);
            daysByDate.TryGetValue(key, out var day);
            var operating = dayOverride?.Kind switch
            {
                DayOverrideKind.Excluded => false,
                DayOverrideKind.Included or DayOverrideKind.Special => true,
                _ => DatabaseService.HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
            };
            // A saved snapshot is historical evidence that this date was used,
            // even if the class's current weekly schedule no longer includes it.
            if (!operating && day is null)
            {
                continue;
            }
            operatingCount++;
            operatingDateKeys.Add(key);
            if (day is null)
            {
                missing.Add(date);
            }
            else
            {
                operatingDayIds.Add(day.Id);
                if (!completedDayIds.Contains(day.Id))
                {
                    incomplete.Add(date);
                }
            }
        }

        var includedDays = days
            .Where(day => operatingDateKeys.Contains(day.DateKey))
            .Where(day => !completedOnly || completedDayIds.Contains(day.Id))
            .ToList();
        var dayPeople = await database.GetClassDayPeopleAsync(includedDays.Select(d => d.Id));
        var records = await database.GetEventRecordsAsync(dayPeople.Select(p => p.Id), eventDefinitionId);
        var peopleById = dayPeople.ToDictionary(p => p.Id);
        var reportRecords = records
            .Where(r => peopleById.ContainsKey(r.ClassDayPersonId))
            .Where(r => !definition.UseExpectedRoster || peopleById[r.ClassDayPersonId].IsExpected)
            .ToList();
        var rows = reportRecords
            .GroupBy(r => peopleById[r.ClassDayPersonId].PersonId)
            .Select(group =>
            {
                var name = group.Select(r => peopleById[r.ClassDayPersonId])
                    .OrderByDescending(p => p.ClassDayId)
                    .First().DisplayNameSnapshot;
                return new EventReportRow
                {
                    PersonId = group.Key,
                    Name = name,
                    Summary = Summarise(group, optionById)
                };
            })
            .OrderBy(r => r.Name)
            .ToList();
        return new EventRangeReport
        {
            Event = definition,
            CalendarDayCount = operatingCount,
            CompletedDayCount = completedDayIds.Count(operatingDayIds.Contains),
            Overall = Summarise(reportRecords, optionById),
            Rows = rows,
            MissingDates = missing,
            IncompleteDates = incomplete
        };
    }

    public static EventSummary Summarise(
        IEnumerable<EventRecord> records,
        IReadOnlyDictionary<int, EventStatusOption> optionById)
    {
        var semantics = records.Select(record =>
            optionById.TryGetValue(record.StatusOptionId, out var option) ? option.Semantic : (EventStatusSemantic?)null).ToList();
        return EventRules.Summarise(semantics);
    }

    public static EventSummary Summarise(
        IEnumerable<DailyEventItem> items,
        IReadOnlyList<EventStatusOption> options) =>
        Summarise(items.Select(i => i.Record), options.ToDictionary(o => o.Id));

    public async Task<TrackingRangeReport> CreateAsync(
        int classGroupId,
        TrackingKind kind,
        DateTime start,
        DateTime end,
        bool completedOnly = true)
    {
        if (end.Date < start.Date)
        {
            (start, end) = (end, start);
        }

        start = start.Date;
        end = end.Date;
        var classGroup = await database.GetClassAsync(classGroupId);
        var days = await database.GetClassDaysAsync(classGroupId, start, end);
        var daysByDate = days.ToDictionary(d => d.DateKey);
        var overrides = await database.GetDayOverridesAsync(classGroupId, start, end);
        var overridesByDate = overrides.ToDictionary(o => o.DateKey);
        var completions = await database.GetCompletionsAsync(days.Select(d => d.Id));
        var completedDayIds = completions.Where(c => c.Kind == kind).Select(c => c.ClassDayId).ToHashSet();

        var calendar = new List<CalendarDateInfo>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var key = DatabaseService.DateKey(date);
            overridesByDate.TryGetValue(key, out var dayOverride);
            daysByDate.TryGetValue(key, out var classDay);
            var operating = dayOverride?.Kind switch
            {
                DayOverrideKind.Excluded => false,
                DayOverrideKind.Included or DayOverrideKind.Special => true,
                _ => DatabaseService.HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
            };
            calendar.Add(new CalendarDateInfo
            {
                Date = date,
                IsOperating = operating,
                Override = dayOverride,
                ClassDay = classDay,
                IsCompleted = classDay is not null && completedDayIds.Contains(classDay.Id)
            });
        }

        var includedDays = days
            .Where(d => !completedOnly || completedDayIds.Contains(d.Id))
            .ToList();
        var dayPeople = await database.GetClassDayPeopleAsync(includedDays.Select(d => d.Id));
        var records = await database.GetTrackingRecordsAsync(dayPeople.Select(p => p.Id), kind);
        var peopleById = dayPeople.ToDictionary(p => p.Id);
        var reportRecords = kind == TrackingKind.Attendance
            ? records.Where(r => peopleById[r.ClassDayPersonId].IsExpected).ToList()
            : records.ToList();

        var rows = reportRecords
            .Where(r => peopleById.ContainsKey(r.ClassDayPersonId))
            .GroupBy(r => peopleById[r.ClassDayPersonId].PersonId)
            .Select(group =>
            {
                var latestName = group
                    .Select(r => peopleById[r.ClassDayPersonId])
                    .OrderByDescending(p => p.ClassDayId)
                    .First().DisplayNameSnapshot;
                return CreateRow(group.Key, latestName, group, kind);
            })
            .OrderBy(r => r.Name)
            .ToList();

        return new TrackingRangeReport
        {
            Kind = kind,
            CalendarDayCount = calendar.Count(c => c.IsOperating),
            IncludedDayCount = includedDays.Count,
            CompletedDayCount = completedDayIds.Count,
            Overall = Summarise(reportRecords, kind),
            Rows = rows,
            Calendar = calendar,
            MissingDates = calendar.Where(c => c.IsMissing).Select(c => c.Date).ToList(),
            IncompleteDates = calendar.Where(c => c.IsIncomplete).Select(c => c.Date).ToList()
        };
    }

    private static TrackingReportRow CreateRow(
        int personId,
        string name,
        IEnumerable<TrackingRecord> records,
        TrackingKind kind)
    {
        var list = records.ToList();
        return new TrackingReportRow
        {
            PersonId = personId,
            Name = name,
            Positive = list.Count(r => r.Status == 1),
            Negative = list.Count(r => r.Status == 2),
            Excused = kind == TrackingKind.Attendance ? list.Count(r => r.Status == 3) : 0,
            Unknown = list.Count(r => r.Status == 0)
        };
    }

    public static TrackingSummary Summarise(IEnumerable<TrackingRecord> records, TrackingKind kind)
        => TrackingRules.Summarise(records.Select(record => record.Status), kind);

    public static TrackingSummary Summarise(IEnumerable<DailyTrackingItem> items, TrackingKind kind) =>
        Summarise(items.Select(i => i.Record), kind);

    // Legacy report retained for callers that have not yet moved to class-scoped reporting.
    public async Task<RangeReport> CreateAsync(DateTime start, DateTime end)
    {
        if (end.Date < start.Date)
        {
            (start, end) = (end, start);
        }
        var sessions = await database.GetSessionsInRangeAsync(start.Date, end.Date);
        var sessionIds = sessions.Select(s => s.Id).ToHashSet();
        var records = (await database.GetAllRecordsAsync()).Where(r => sessionIds.Contains(r.SessionId)).ToList();
        var people = await database.GetPeopleAsync(includeArchived: true);
        var peopleById = people.ToDictionary(p => p.Id);
        var rows = records.Where(r => peopleById.ContainsKey(r.PersonId))
            .GroupBy(r => r.PersonId)
            .Select(group => new ReportRow
            {
                Name = peopleById[group.Key].DisplayName,
                Present = group.Count(r => r.Status == AttendanceStatus.Present),
                Absent = group.Count(r => r.Status == AttendanceStatus.Absent),
                Excused = group.Count(r => r.Status == AttendanceStatus.Excused),
                Unknown = group.Count(r => r.Status == AttendanceStatus.Unknown)
            }).OrderBy(r => r.Name).ToList();
        return new RangeReport
        {
            SessionCount = sessions.Count,
            Overall = Summarise(records),
            Rows = rows
        };
    }

    public static AttendanceSummary Summarise(IEnumerable<AttendanceRecord> records)
    {
        var list = records.ToList();
        return new AttendanceSummary(
            list.Count(r => r.Status == AttendanceStatus.Present),
            list.Count(r => r.Status == AttendanceStatus.Absent),
            list.Count(r => r.Status == AttendanceStatus.Excused),
            list.Count(r => r.Status == AttendanceStatus.Unknown));
    }

    public static AttendanceSummary Summarise(IEnumerable<AttendanceItem> items) =>
        Summarise(items.Select(i => i.Record));
}

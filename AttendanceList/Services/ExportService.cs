using AttendanceList.Models;
using Microsoft.Maui.Storage;

namespace AttendanceList.Services;

public sealed class ExportService
{
    private readonly DatabaseService database;
    private readonly string? _outputDirectory;

    public ExportService(DatabaseService database)
    {
        this.database = database;
    }

    internal ExportService(DatabaseService database, string outputDirectory)
    {
        this.database = database;
        _outputDirectory = outputDirectory;
    }

    public async Task<string> ExportAsync(
        int classGroupId,
        DateTime start,
        DateTime end,
        ExportProfileDefinition profile)
    {
        if (end.Date < start.Date)
        {
            (start, end) = (end, start);
        }
        start = start.Date;
        end = end.Date;
        var classGroup = await database.GetClassAsync(classGroupId);
        var organization = await database.GetOrganizationAsync(classGroup.OrganizationId);
        var classEvents = await database.GetEventsForClassAsync(classGroupId, includeDisabled: true);
        var selected = classEvents.FirstOrDefault(e => e.Event.Id == profile.EventDefinitionId)
            ?? classEvents.FirstOrDefault(e => e.Event.SystemKey == (profile.Kind == TrackingKind.Meal ? "meal" : "attendance"))
            ?? throw new InvalidOperationException(LocalizationService.T("NoEventsEnabled"));
        profile.EventDefinitionId = selected.Event.Id;

        var days = await database.GetClassDaysAsync(classGroupId, start, end);
        var daysByDate = days.ToDictionary(d => d.DateKey);
        var overrides = await database.GetDayOverridesAsync(classGroupId, start, end);
        var overridesByDate = overrides.ToDictionary(o => o.DateKey);
        var completions = await database.GetEventCompletionsAsync(days.Select(d => d.Id));
        var completedDayIds = completions
            .Where(c => c.EventDefinitionId == selected.Event.Id)
            .Select(c => c.ClassDayId)
            .ToHashSet();
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
            if (operating || classDay is not null)
            {
                calendar.Add(new CalendarDateInfo
                {
                    Date = date,
                    // Existing historical snapshots remain reportable after a
                    // class schedule changes. Explicit exclusions are still
                    // removed from the regular summary by BuildSummary.
                    IsOperating = operating || classDay is not null,
                    Override = dayOverride,
                    ClassDay = classDay,
                    IsCompleted = classDay is not null && completedDayIds.Contains(classDay.Id)
                });
            }
        }

        var dayPeople = await database.GetClassDayPeopleAsync(days.Select(d => d.Id));
        var records = await database.GetEventRecordsAsync(dayPeople.Select(p => p.Id), selected.Event.Id);
        var members = await database.GetClassMembersAsync(classGroupId, includeArchived: true);
        var membershipPeriods = await database.GetMembershipPeriodsAsync(
            members.Select(member => member.Membership.Id));
        var people = BuildPeople(members, membershipPeriods, dayPeople);
        var dayPeopleById = dayPeople.ToDictionary(p => p.Id);
        var recordByDayPerson = records
            .Where(r => dayPeopleById.ContainsKey(r.ClassDayPersonId))
            .ToDictionary(
                r => (dayPeopleById[r.ClassDayPersonId].ClassDayId, dayPeopleById[r.ClassDayPersonId].PersonId),
                r => r);
        var dayPersonByDayPerson = dayPeople.ToDictionary(p => (p.ClassDayId, p.PersonId));
        var optionById = selected.StatusOptions.ToDictionary(o => o.Id);
        var includedCalendar = profile.CompletedOnly
            ? calendar.Where(value =>
                    value.ClassDay is not null && completedDayIds.Contains(value.ClassDay.Id))
                .ToList()
            : calendar;
        var sheets = new List<XlsxSheet>
        {
            BuildMatrix(organization, classGroup, selected.Event, selected.StatusOptions, start, end, profile,
                includedCalendar, people, recordByDayPerson, dayPersonByDayPerson, optionById, completedDayIds),
            BuildSummary(organization, classGroup, selected.Event, start, end, profile,
                includedCalendar, people, recordByDayPerson, dayPersonByDayPerson, optionById, completedDayIds),
            BuildExceptions(organization, classGroup, selected.Event, profile,
                includedCalendar, people, recordByDayPerson, dayPersonByDayPerson, optionById, completedDayIds)
        };
        if (profile.IncludeRawData)
        {
            sheets.Add(BuildRaw(organization, classGroup, selected.Event, profile,
                days, dayPeople, records, optionById, completedDayIds));
        }
        var cleanName = string.Concat(classGroup.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var cleanEvent = string.Concat(selected.Event.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(
            _outputDirectory ?? FileSystem.CacheDirectory,
            $"{cleanName}_{cleanEvent}_{start:yyyyMMdd}-{end:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx");
        SimpleXlsxWriter.Write(path, sheets);
        return path;
    }

    private static List<ExportPerson> BuildPeople(
        IReadOnlyList<ClassMember> members,
        IReadOnlyList<ClassMembershipPeriod> membershipPeriods,
        IReadOnlyList<ClassDayPerson> dayPeople)
    {
        var periodsByMembership = membershipPeriods
            .GroupBy(period => period.ClassMembershipId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ClassMembershipPeriod>)group.ToList());
        var result = members.Select(m => new ExportPerson(
            m.Person.Id,
            m.Person.DisplayName,
            m.Membership.SortOrder,
            m.Membership,
            periodsByMembership.GetValueOrDefault(m.Membership.Id) ?? [])).ToDictionary(p => p.Id);
        foreach (var snapshot in dayPeople.OrderBy(p => p.ClassDayId))
        {
            if (!result.ContainsKey(snapshot.PersonId))
            {
                result[snapshot.PersonId] = new ExportPerson(
                    snapshot.PersonId, snapshot.DisplayNameSnapshot, snapshot.SortOrderSnapshot, null, []);
            }
        }
        return result.Values.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList();
    }

    private static XlsxSheet BuildMatrix(
        Organization organization,
        ClassGroup classGroup,
        EventDefinition definition,
        IReadOnlyList<EventStatusOption> options,
        DateTime start,
        DateTime end,
        ExportProfileDefinition profile,
        IReadOnlyList<CalendarDateInfo> calendar,
        IReadOnlyList<ExportPerson> people,
        IReadOnlyDictionary<(int DayId, int PersonId), EventRecord> records,
        IReadOnlyDictionary<(int DayId, int PersonId), ClassDayPerson> dayPeople,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        var sheet = new XlsxSheet { Name = LocalizationService.T("AttendanceMatrix") };
        var title = $"{organization.Name} · {classGroup.Name} · {definition.Name} · {LocalizationService.FormatDate(start)}–{LocalizationService.FormatDate(end)}";
        if (profile.DatesAsRows)
        {
            sheet.AddRow([title], style: 1);
            sheet.Merges.Add($"A1:{ColumnName(people.Count + 1)}1");
            sheet.AddRow(new[] { LocalizationService.T("Date") }.Concat(people.Select(p => p.Name)), style: 2);
            foreach (var date in calendar)
            {
                var row = new List<object?> { date.Date };
                row.AddRange(people.Select(person => MatrixValue(
                    date, person, definition, profile, records, dayPeople, optionById, completedDayIds)));
                sheet.AddRow(row);
            }
        }
        else
        {
            sheet.AddRow([title], style: 1);
            sheet.Merges.Add($"A1:{ColumnName(calendar.Count + 2)}1");
            sheet.AddRow(new[] { SubjectLabels.ForClass(classGroup) }
                .Concat(calendar.Select(c => c.Date.ToString("yyyy-MM-dd")))
                .Concat([LocalizationService.T("Total")]), style: 2);
            foreach (var person in people)
            {
                var values = calendar.Select(date => MatrixValue(
                    date, person, definition, profile, records, dayPeople, optionById, completedDayIds)).ToList();
                sheet.AddRow(new[] { person.Name }.Concat(values).Concat([values.Count(v => v == profile.PositiveSymbol).ToString()]));
            }
        }
        sheet.AddRow(Array.Empty<object?>());
        var legend = string.Join(" · ", options.Select(option =>
            $"{OptionSymbol(option, profile)}={option.Label}"));
        sheet.AddRow([LocalizationService.T("Legend"),
            $"{legend} · ?={LocalizationService.T("Unknown")} · !={LocalizationService.T("Missing")} · ~={LocalizationService.T("Incomplete")}"], style: 4);
        return sheet;
    }

    private static string MatrixValue(
        CalendarDateInfo date,
        ExportPerson person,
        EventDefinition definition,
        ExportProfileDefinition profile,
        IReadOnlyDictionary<(int DayId, int PersonId), EventRecord> records,
        IReadOnlyDictionary<(int DayId, int PersonId), ClassDayPerson> dayPeople,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        if (date.ClassDay is null)
        {
            return Expected(person, date.Date, date.IsOperating, definition.UseExpectedRoster) ? "!" : string.Empty;
        }
        if (profile.CompletedOnly && !completedDayIds.Contains(date.ClassDay.Id))
        {
            return "~";
        }
        if (!records.TryGetValue((date.ClassDay.Id, person.Id), out var record))
        {
            return string.Empty;
        }
        dayPeople.TryGetValue((date.ClassDay.Id, person.Id), out var dayPerson);
        if (definition.UseExpectedRoster && dayPerson is { IsExpected: false } && record.StatusOptionId == 0)
        {
            return string.Empty;
        }
        return optionById.TryGetValue(record.StatusOptionId, out var option)
            ? OptionSymbol(option, profile)
            : "?";
    }

    private static XlsxSheet BuildSummary(
        Organization organization,
        ClassGroup classGroup,
        EventDefinition definition,
        DateTime start,
        DateTime end,
        ExportProfileDefinition profile,
        IReadOnlyList<CalendarDateInfo> calendar,
        IReadOnlyList<ExportPerson> people,
        IReadOnlyDictionary<(int DayId, int PersonId), EventRecord> records,
        IReadOnlyDictionary<(int DayId, int PersonId), ClassDayPerson> dayPeople,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        var sheet = new XlsxSheet { Name = LocalizationService.T("CustomSummary") };
        sheet.AddRow([$"{organization.Name} · {classGroup.Name} · {definition.Name} · {LocalizationService.FormatDate(start)}–{LocalizationService.FormatDate(end)}"], style: 1);
        sheet.Merges.Add($"A1:{ColumnName(profile.Buckets.Count + 2)}1");
        sheet.AddRow(new[] { SubjectLabels.ForClass(classGroup), profile.BaseBucketName }
            .Concat(profile.Buckets.Select(b => b.Name)), style: 2);
        var baseDates = calendar
            .Where(c => c.IsOperating && c.Override?.Kind != DayOverrideKind.Excluded)
            .Where(c => !profile.Buckets.Any(bucket => bucket.ExcludeFromBase && Matches(bucket, c)))
            .ToList();
        foreach (var person in people)
        {
            var values = new List<object?>
            {
                person.Name,
                CountPositive(baseDates, person.Id, definition, profile, records, dayPeople, optionById, completedDayIds)
            };
            values.AddRange(profile.Buckets.Select(bucket => (object?)CountPositive(
                calendar.Where(c => Matches(bucket, c)), person.Id, definition, profile, records, dayPeople, optionById, completedDayIds)));
            sheet.AddRow(values);
        }
        return sheet;
    }

    private static int CountPositive(
        IEnumerable<CalendarDateInfo> dates,
        int personId,
        EventDefinition definition,
        ExportProfileDefinition profile,
        IReadOnlyDictionary<(int DayId, int PersonId), EventRecord> records,
        IReadOnlyDictionary<(int DayId, int PersonId), ClassDayPerson> dayPeople,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        return dates.Count(date =>
            date.ClassDay is not null
            && (!profile.CompletedOnly || completedDayIds.Contains(date.ClassDay.Id))
            && (!definition.UseExpectedRoster
                || (dayPeople.TryGetValue((date.ClassDay.Id, personId), out var person) && person.IsExpected))
            && records.TryGetValue((date.ClassDay.Id, personId), out var record)
            && optionById.TryGetValue(record.StatusOptionId, out var option)
            && option.Semantic == EventStatusSemantic.Positive);
    }

    private static XlsxSheet BuildExceptions(
        Organization organization,
        ClassGroup classGroup,
        EventDefinition definition,
        ExportProfileDefinition profile,
        IReadOnlyList<CalendarDateInfo> calendar,
        IReadOnlyList<ExportPerson> people,
        IReadOnlyDictionary<(int DayId, int PersonId), EventRecord> records,
        IReadOnlyDictionary<(int DayId, int PersonId), ClassDayPerson> dayPeople,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        var sheet = new XlsxSheet { Name = LocalizationService.T("Exceptions") };
        sheet.AddRow([
            LocalizationService.T("Date"), LocalizationService.T("Organization"), LocalizationService.T("Class"),
            SubjectLabels.ForClass(classGroup), LocalizationService.T("Expected"), LocalizationService.T("RecordType"),
            LocalizationService.T("Status"), LocalizationService.T("DayLabel"), LocalizationService.T("Note"),
            LocalizationService.T("RosterSource")
        ], style: 2);
        foreach (var date in calendar)
        {
            if (date.ClassDay is null)
            {
                sheet.AddRow([date.Date, organization.Name, classGroup.Name, string.Empty,
                    LocalizationService.T("Yes"), definition.Name, LocalizationService.T("Missing"),
                    date.Override?.Label ?? string.Empty, string.Empty, string.Empty], style: 4);
                continue;
            }
            foreach (var person in people)
            {
                if (!records.TryGetValue((date.ClassDay.Id, person.Id), out var record)
                    || !dayPeople.TryGetValue((date.ClassDay.Id, person.Id), out var dayPerson))
                {
                    continue;
                }
                optionById.TryGetValue(record.StatusOptionId, out var option);
                var relevant = !definition.UseExpectedRoster || dayPerson.IsExpected;
                var exceptional = (relevant && option?.Semantic != EventStatusSemantic.Positive)
                    || (!relevant && record.StatusOptionId != 0)
                    || !string.IsNullOrWhiteSpace(record.Note);
                if (!exceptional)
                {
                    continue;
                }
                var status = profile.CompletedOnly && !completedDayIds.Contains(date.ClassDay.Id)
                    ? LocalizationService.T("Incomplete")
                    : option?.Label ?? LocalizationService.T("Unknown");
                var organizationName = string.IsNullOrWhiteSpace(date.ClassDay.OrganizationNameSnapshot)
                    ? organization.Name
                    : date.ClassDay.OrganizationNameSnapshot;
                var className = string.IsNullOrWhiteSpace(date.ClassDay.ClassNameSnapshot)
                    ? classGroup.Name
                    : date.ClassDay.ClassNameSnapshot;
                sheet.AddRow([date.Date, organizationName, className, dayPerson.DisplayNameSnapshot,
                    relevant ? LocalizationService.T("Yes") : LocalizationService.T("No"), definition.Name,
                    status, date.Override?.Label ?? string.Empty, profile.IncludeNotes ? record.Note : string.Empty,
                    RosterSourceText(date.ClassDay.RosterSource)]);
            }
        }
        return sheet;
    }

    private static XlsxSheet BuildRaw(
        Organization organization,
        ClassGroup classGroup,
        EventDefinition definition,
        ExportProfileDefinition profile,
        IReadOnlyList<ClassDay> days,
        IReadOnlyList<ClassDayPerson> dayPeople,
        IReadOnlyList<EventRecord> records,
        IReadOnlyDictionary<int, EventStatusOption> optionById,
        IReadOnlySet<int> completedDayIds)
    {
        var sheet = new XlsxSheet { Name = LocalizationService.T("RawData") };
        sheet.AddRow([
            LocalizationService.T("Date"), LocalizationService.T("Organization"), LocalizationService.T("Class"),
            SubjectLabels.ForClass(classGroup), LocalizationService.T("Expected"), LocalizationService.T("RecordType"),
            LocalizationService.T("Status"), LocalizationService.T("Completed"), LocalizationService.T("Note"),
            LocalizationService.T("RosterSource")
        ], style: 2);
        var daysById = days.ToDictionary(d => d.Id);
        var peopleById = dayPeople.ToDictionary(p => p.Id);
        foreach (var record in records.Where(r => peopleById.ContainsKey(r.ClassDayPersonId))
                     .Where(r => !profile.CompletedOnly
                         || completedDayIds.Contains(peopleById[r.ClassDayPersonId].ClassDayId))
                     .OrderBy(r => peopleById[r.ClassDayPersonId].ClassDayId)
                     .ThenBy(r => peopleById[r.ClassDayPersonId].SortOrderSnapshot))
        {
            var person = peopleById[record.ClassDayPersonId];
            var day = daysById[person.ClassDayId];
            optionById.TryGetValue(record.StatusOptionId, out var option);
            var organizationName = string.IsNullOrWhiteSpace(day.OrganizationNameSnapshot)
                ? organization.Name
                : day.OrganizationNameSnapshot;
            var className = string.IsNullOrWhiteSpace(day.ClassNameSnapshot)
                ? classGroup.Name
                : day.ClassNameSnapshot;
            sheet.AddRow([
                DateTime.ParseExact(day.DateKey, "yyyy-MM-dd", null), organizationName, className,
                person.DisplayNameSnapshot, person.IsExpected ? LocalizationService.T("Yes") : LocalizationService.T("No"),
                definition.Name, option?.Label ?? LocalizationService.T("Unknown"),
                completedDayIds.Contains(day.Id) ? LocalizationService.T("Yes") : LocalizationService.T("No"),
                profile.IncludeNotes ? record.Note : string.Empty,
                RosterSourceText(day.RosterSource)
            ]);
        }
        return sheet;
    }

    private static string RosterSourceText(RosterSource source) => LocalizationService.T(source switch
    {
        RosterSource.HistoricalMembership => "RosterSourceHistorical",
        RosterSource.ManualSelection => "RosterSourceManual",
        RosterSource.CurrentRosterAssumption => "RosterSourceCurrentAssumption",
        _ => "RosterSourceLegacy"
    });

    private static bool Matches(ExportBucketDefinition bucket, CalendarDateInfo date)
    {
        return bucket.Weekdays.Contains((int)date.Date.DayOfWeek)
            || bucket.DateKeys.Contains(DatabaseService.DateKey(date.Date), StringComparer.Ordinal)
            || (!string.IsNullOrWhiteSpace(date.Override?.Label)
                && bucket.Labels.Contains(date.Override.Label, StringComparer.OrdinalIgnoreCase));
    }

    private static bool Expected(ExportPerson person, DateTime date, bool operating, bool useRoster)
    {
        if (!operating || person.Membership is null)
        {
            return false;
        }
        if (!useRoster)
        {
            return true;
        }
        var key = DatabaseService.DateKey(date);
        return MembershipRules.IncludesDate(person.Membership, person.Periods, key)
            && DatabaseService.HasDay(person.Membership.ExpectedDaysMask, date.DayOfWeek);
    }

    private static string OptionSymbol(EventStatusOption option, ExportProfileDefinition profile) =>
        option.Semantic == EventStatusSemantic.Positive
            ? profile.PositiveSymbol
            : string.IsNullOrWhiteSpace(option.Symbol) ? "•" : option.Symbol;

    private static string ColumnName(int number)
    {
        var value = string.Empty;
        while (number > 0)
        {
            number--;
            value = (char)('A' + number % 26) + value;
            number /= 26;
        }
        return value;
    }

    private sealed record ExportPerson(
        int Id,
        string Name,
        int SortOrder,
        ClassMembership? Membership,
        IReadOnlyList<ClassMembershipPeriod> Periods);
}

using System.Text.Json;
using AttendanceList.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace AttendanceList.Services;

public sealed class DatabaseService
{
    private const string SchemaVersionKey = "SchemaVersion";
    private const string CurrentSchemaVersion = "7";
    private readonly AsyncInitializationGate<SQLiteAsyncConnection> _databaseGate = new();
    private readonly string? _databasePath;
    private readonly Func<string, string> _translate;
    private readonly bool _createMigrationBackup;
    private readonly bool _localizeAfterInitialization;

    public DatabaseService()
    {
        _translate = LocalizationService.T;
        _createMigrationBackup = true;
        _localizeAfterInitialization = true;
    }

    internal DatabaseService(string databasePath)
    {
        _databasePath = databasePath;
        _translate = key => key;
        _createMigrationBackup = false;
        _localizeAfterInitialization = false;
    }

    private Task<SQLiteAsyncConnection> GetDatabaseAsync() =>
        _databaseGate.GetAsync(InitialiseDatabaseAsync);

    private async Task<SQLiteAsyncConnection> InitialiseDatabaseAsync()
    {
        var path = _databasePath ?? Path.Combine(FileSystem.AppDataDirectory, "attendance.db3");
        if (_createMigrationBackup)
        {
            BackupBeforeSchemaMigration(path);
        }
        var database = new SQLiteAsyncConnection(
            path,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        try
        {
            await CreateSchemaAsync(database);
            await EnsureDefaultContextAndMigrateAsync(database, _translate);
            if (_localizeAfterInitialization)
            {
                await LocalizeUntouchedDefaultsAsync(database);
            }
            return database;
        }
        catch
        {
            await database.CloseAsync();
            throw;
        }
    }

    private static void BackupBeforeSchemaMigration(string path)
    {
        const string preferenceKey = "schema_v7_backup_created";
        if (!File.Exists(path) || Preferences.Default.Get(preferenceKey, false))
        {
            return;
        }

        var backupPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"attendance.pre-v7.{DateTime.Now:yyyyMMddHHmmss}.db3");
        File.Copy(path, backupPath, overwrite: false);
        Preferences.Default.Set(preferenceKey, true);
    }

    private static async Task CreateSchemaAsync(SQLiteAsyncConnection db)
    {
        // Legacy tables remain readable so existing installations can be migrated safely.
        await db.CreateTableAsync<Person>();
        await db.CreateTableAsync<AttendanceSession>();
        await db.CreateTableAsync<AttendanceRecord>();

        await db.CreateTableAsync<Organization>();
        await db.CreateTableAsync<ClassGroup>();
        await db.CreateTableAsync<ClassMembership>();
        await db.CreateTableAsync<ClassMembershipPeriod>();
        await db.CreateTableAsync<DayOverride>();
        await db.CreateTableAsync<ClassDay>();
        await db.CreateTableAsync<ClassDayPerson>();
        await db.CreateTableAsync<TrackingRecord>();
        await db.CreateTableAsync<TrackingCompletion>();
        await db.CreateTableAsync<EventDefinition>();
        await db.CreateTableAsync<EventStatusOption>();
        await db.CreateTableAsync<ClassEvent>();
        await db.CreateTableAsync<EventRecord>();
        await db.CreateTableAsync<EventCompletion>();
        await db.CreateTableAsync<EventReminder>();
        await db.CreateTableAsync<ExportProfile>();
        await db.CreateTableAsync<AppMetadata>();

        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AttendanceRecords_Session_Person " +
            "ON AttendanceRecords(SessionId, PersonId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassMemberships_Class_Person " +
            "ON ClassMemberships(ClassGroupId, PersonId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassMembershipPeriods_Membership_Start " +
            "ON ClassMembershipPeriods(ClassMembershipId, StartDateKey)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_DayOverrides_Class_Date " +
            "ON DayOverrides(ClassGroupId, DateKey)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassDays_Class_Date " +
            "ON ClassDays(ClassGroupId, DateKey)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassDayPeople_Day_Person " +
            "ON ClassDayPeople(ClassDayId, PersonId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_TrackingRecords_Person_Kind " +
            "ON TrackingRecords(ClassDayPersonId, Kind)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_TrackingCompletions_Day_Kind " +
            "ON TrackingCompletions(ClassDayId, Kind)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_EventDefinitions_Organization_Name " +
            "ON EventDefinitions(OrganizationId, Name)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassEvents_Class_Event " +
            "ON ClassEvents(ClassGroupId, EventDefinitionId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_EventRecords_Person_Event " +
            "ON EventRecords(ClassDayPersonId, EventDefinitionId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_EventCompletions_Day_Event " +
            "ON EventCompletions(ClassDayId, EventDefinitionId)");
        await db.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ExportProfiles_Class_Name " +
            "ON ExportProfiles(ClassGroupId, Name)");
    }

    private static async Task EnsureDefaultContextAndMigrateAsync(
        SQLiteAsyncConnection db,
        Func<string, string> translate)
    {
        var organization = await db.Table<Organization>().FirstOrDefaultAsync();
        if (organization is null)
        {
            organization = new Organization { Name = translate("DefaultOrganization") };
            await db.InsertAsync(organization);
        }

        var classGroup = await db.Table<ClassGroup>().FirstOrDefaultAsync();
        if (classGroup is null)
        {
            classGroup = new ClassGroup
            {
                OrganizationId = organization.Id,
                Name = translate("DefaultClass"),
                OperatingDaysMask = 127
            };
            await db.InsertAsync(classGroup);
        }

        var metadata = await db.FindAsync<AppMetadata>(SchemaVersionKey);
        _ = int.TryParse(metadata?.Value, out var schemaVersion);
        if (schemaVersion >= int.Parse(CurrentSchemaVersion))
        {
            return;
        }

        if (schemaVersion < 2)
        {
        var people = await db.Table<Person>().ToListAsync();
        var memberships = await db.Table<ClassMembership>()
            .Where(m => m.ClassGroupId == classGroup.Id)
            .ToListAsync();
        var membershipPersonIds = memberships.Select(m => m.PersonId).ToHashSet();

        foreach (var person in people.Where(p => !membershipPersonIds.Contains(p.Id)))
        {
            await db.InsertAsync(new ClassMembership
            {
                ClassGroupId = classGroup.Id,
                PersonId = person.Id,
                IsActive = person.IsActive,
                SortOrder = person.SortOrder,
                ExpectedDaysMask = classGroup.OperatingDaysMask,
                StartDateKey = DateTime.Today.ToString("yyyy-MM-dd"),
                EndDateKey = person.IsActive ? null : DateTime.Today.ToString("yyyy-MM-dd")
            });
        }

        var sessions = await db.Table<AttendanceSession>().ToListAsync();
        foreach (var session in sessions)
        {
            var day = await db.Table<ClassDay>()
                .Where(d => d.ClassGroupId == classGroup.Id && d.DateKey == session.SessionDate)
                .FirstOrDefaultAsync();
            if (day is null)
            {
                day = new ClassDay
                {
                    ClassGroupId = classGroup.Id,
                    DateKey = session.SessionDate,
                    CreatedAtIso = session.StartedAtIso
                };
                await db.InsertAsync(day);
            }

            var legacyRecords = await db.Table<AttendanceRecord>()
                .Where(r => r.SessionId == session.Id)
                .ToListAsync();
            foreach (var legacyRecord in legacyRecords)
            {
                var person = people.FirstOrDefault(p => p.Id == legacyRecord.PersonId);
                if (person is null)
                {
                    continue;
                }

                var dayPerson = await db.Table<ClassDayPerson>()
                    .Where(p => p.ClassDayId == day.Id && p.PersonId == person.Id)
                    .FirstOrDefaultAsync();
                if (dayPerson is null)
                {
                    dayPerson = new ClassDayPerson
                    {
                        ClassDayId = day.Id,
                        PersonId = person.Id,
                        DisplayNameSnapshot = person.DisplayName,
                        IsExpected = true,
                        SortOrderSnapshot = person.SortOrder
                    };
                    await db.InsertAsync(dayPerson);
                }

                var migrated = await db.Table<TrackingRecord>()
                    .Where(r => r.ClassDayPersonId == dayPerson.Id && r.Kind == TrackingKind.Attendance)
                    .FirstOrDefaultAsync();
                if (migrated is null)
                {
                    await db.InsertAsync(new TrackingRecord
                    {
                        ClassDayPersonId = dayPerson.Id,
                        Kind = TrackingKind.Attendance,
                        Status = (int)legacyRecord.Status,
                        RecordedAtIso = legacyRecord.RecordedAtIso,
                        UpdatedAtIso = legacyRecord.UpdatedAtIso
                    });
                }
            }

            if (session.IsFinalised)
            {
                var completion = await db.Table<TrackingCompletion>()
                    .Where(c => c.ClassDayId == day.Id && c.Kind == TrackingKind.Attendance)
                    .FirstOrDefaultAsync();
                if (completion is null)
                {
                    await db.InsertAsync(new TrackingCompletion
                    {
                        ClassDayId = day.Id,
                        Kind = TrackingKind.Attendance,
                        CompletedAtIso = session.CompletedAtIso!
                    });
                }
            }
        }

        }

        if (schemaVersion < 3)
        {
            await MigrateToEventsAsync(db, translate);
        }

        if (schemaVersion < 4)
        {
            await MigrateMembershipPeriodsAsync(db);
        }

        if (schemaVersion < 5)
        {
            await MigrateDaySnapshotsAsync(db);
        }

        if (schemaVersion < 6)
        {
            await MigrateSubjectLabelsAsync(db);
        }

        if (schemaVersion < 7)
        {
            await MigrateEventNotesAsync(db);
        }

        await db.InsertOrReplaceAsync(new AppMetadata
        {
            Key = SchemaVersionKey,
            Value = CurrentSchemaVersion
        });
    }

    private static async Task MigrateMembershipPeriodsAsync(SQLiteAsyncConnection db)
    {
        var memberships = await db.Table<ClassMembership>().ToListAsync();
        var existingMembershipIds = (await db.Table<ClassMembershipPeriod>().ToListAsync())
            .Select(period => period.ClassMembershipId)
            .ToHashSet();
        foreach (var membership in memberships.Where(value => !existingMembershipIds.Contains(value.Id)))
        {
            await db.InsertAsync(new ClassMembershipPeriod
            {
                ClassMembershipId = membership.Id,
                StartDateKey = membership.StartDateKey,
                EndDateKey = membership.EndDateKey
            });
        }
    }

    private static async Task MigrateDaySnapshotsAsync(SQLiteAsyncConnection db)
    {
        var organizations = (await db.Table<Organization>().ToListAsync())
            .ToDictionary(value => value.Id);
        var classes = (await db.Table<ClassGroup>().ToListAsync())
            .ToDictionary(value => value.Id);
        var dayPeopleCounts = (await db.Table<ClassDayPerson>().ToListAsync())
            .GroupBy(value => value.ClassDayId)
            .ToDictionary(group => group.Key, group => group.Count());
        var days = await db.Table<ClassDay>().ToListAsync();
        foreach (var day in days)
        {
            if (classes.TryGetValue(day.ClassGroupId, out var classGroup))
            {
                day.ClassNameSnapshot = string.IsNullOrWhiteSpace(day.ClassNameSnapshot)
                    ? classGroup.Name
                    : day.ClassNameSnapshot;
                if (organizations.TryGetValue(classGroup.OrganizationId, out var organization))
                {
                    day.OrganizationNameSnapshot = string.IsNullOrWhiteSpace(day.OrganizationNameSnapshot)
                        ? organization.Name
                        : day.OrganizationNameSnapshot;
                }
            }

            if (day.RosterSource == RosterSource.Unspecified
                && dayPeopleCounts.GetValueOrDefault(day.Id) > 0)
            {
                day.RosterSource = RosterSource.LegacySnapshot;
                day.RosterConfirmedAtIso ??= day.CreatedAtIso;
            }
            await db.UpdateAsync(day);
        }
    }

    private static async Task MigrateSubjectLabelsAsync(SQLiteAsyncConnection db)
    {
        var classes = (await db.Table<ClassGroup>().ToListAsync())
            .ToDictionary(value => value.Id);
        var days = await db.Table<ClassDay>().ToListAsync();
        foreach (var day in days.Where(value => string.IsNullOrWhiteSpace(value.SubjectLabelSnapshot)))
        {
            if (classes.TryGetValue(day.ClassGroupId, out var classGroup))
            {
                day.SubjectLabelSnapshot = classGroup.SubjectLabel;
                await db.UpdateAsync(day);
            }
        }
    }

    private static async Task MigrateEventNotesAsync(SQLiteAsyncConnection db)
    {
        var legacyNotes = (await db.Table<ClassDayPerson>().ToListAsync())
            .Where(value => !string.IsNullOrWhiteSpace(value.Note))
            .ToList();
        if (legacyNotes.Count == 0)
        {
            return;
        }

        var days = (await db.Table<ClassDay>().ToListAsync()).ToDictionary(value => value.Id);
        var classes = (await db.Table<ClassGroup>().ToListAsync()).ToDictionary(value => value.Id);
        var attendanceEvents = (await db.Table<EventDefinition>().ToListAsync())
            .Where(value => value.SystemKey == "attendance")
            .GroupBy(value => value.OrganizationId)
            .ToDictionary(group => group.Key, group => group.First().Id);
        var recordsByPerson = (await db.Table<EventRecord>().ToListAsync())
            .GroupBy(value => value.ClassDayPersonId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.EventDefinitionId).ToList());

        foreach (var person in legacyNotes)
        {
            if (!recordsByPerson.TryGetValue(person.Id, out var records) || records.Count == 0)
            {
                continue;
            }
            EventRecord? target = null;
            if (days.TryGetValue(person.ClassDayId, out var day)
                && classes.TryGetValue(day.ClassGroupId, out var classGroup)
                && attendanceEvents.TryGetValue(classGroup.OrganizationId, out var attendanceEventId))
            {
                target = records.FirstOrDefault(value => value.EventDefinitionId == attendanceEventId);
            }
            target ??= records[0];
            if (string.IsNullOrWhiteSpace(target.Note))
            {
                target.Note = person.Note.Trim();
                await db.UpdateAsync(target);
            }
        }
    }

    private static async Task MigrateToEventsAsync(SQLiteAsyncConnection db, Func<string, string> translate)
    {
        var organizations = await db.Table<Organization>().ToListAsync();
        foreach (var organization in organizations)
        {
            await EnsureOrganizationEventsAsync(db, organization.Id, translate);
        }

        var classes = await db.Table<ClassGroup>().ToListAsync();
        var events = await db.Table<EventDefinition>().ToListAsync();
        foreach (var classGroup in classes)
        {
            var builtIns = events
                .Where(e => e.OrganizationId == classGroup.OrganizationId && !string.IsNullOrEmpty(e.SystemKey))
                .OrderBy(e => e.SortOrder)
                .ToList();
            foreach (var definition in builtIns)
            {
                await EnsureClassEventAsync(db, classGroup.Id, definition.Id, definition.SortOrder, true);
            }
        }

        var days = await db.Table<ClassDay>().ToListAsync();
        var classesById = classes.ToDictionary(c => c.Id);
        var dayPeople = await db.Table<ClassDayPerson>().ToListAsync();
        var dayByPersonId = dayPeople.ToDictionary(p => p.Id, p => days.First(d => d.Id == p.ClassDayId));
        var options = await db.Table<EventStatusOption>().ToListAsync();
        var trackingRecords = await db.Table<TrackingRecord>().ToListAsync();
        foreach (var source in trackingRecords)
        {
            if (!dayByPersonId.TryGetValue(source.ClassDayPersonId, out var day)
                || !classesById.TryGetValue(day.ClassGroupId, out var classGroup))
            {
                continue;
            }
            var systemKey = source.Kind == TrackingKind.Attendance ? "attendance" : "meal";
            var definition = events.First(e => e.OrganizationId == classGroup.OrganizationId && e.SystemKey == systemKey);
            var statusOptionId = source.Status == 0
                ? 0
                : options.Where(o => o.EventDefinitionId == definition.Id)
                    .OrderBy(o => o.SortOrder)
                    .ElementAtOrDefault(source.Status - 1)?.Id ?? 0;
            await db.InsertOrReplaceAsync(new EventRecord
            {
                ClassDayPersonId = source.ClassDayPersonId,
                EventDefinitionId = definition.Id,
                StatusOptionId = statusOptionId,
                RecordedAtIso = source.RecordedAtIso,
                UpdatedAtIso = source.UpdatedAtIso
            });
        }

        var completions = await db.Table<TrackingCompletion>().ToListAsync();
        foreach (var source in completions)
        {
            var day = days.FirstOrDefault(d => d.Id == source.ClassDayId);
            if (day is null || !classesById.TryGetValue(day.ClassGroupId, out var classGroup))
            {
                continue;
            }
            var systemKey = source.Kind == TrackingKind.Attendance ? "attendance" : "meal";
            var definition = events.First(e => e.OrganizationId == classGroup.OrganizationId && e.SystemKey == systemKey);
            await db.InsertOrReplaceAsync(new EventCompletion
            {
                ClassDayId = source.ClassDayId,
                EventDefinitionId = definition.Id,
                CompletedAtIso = source.CompletedAtIso
            });
        }
    }

    private static async Task EnsureOrganizationEventsAsync(
        SQLiteAsyncConnection db,
        int organizationId,
        Func<string, string>? translator = null)
    {
        var translate = translator ?? LocalizationService.T;
        await EnsureBuiltInEventAsync(
            db, organizationId, "attendance", translate("Attendance"), true, 0,
            (translate("Present"), "✓", EventStatusSemantic.Positive),
            (translate("Absent"), "×", EventStatusSemantic.Negative),
            (translate("Excused"), "E", EventStatusSemantic.Excused));
        await EnsureBuiltInEventAsync(
            db, organizationId, "meal", translate("Meals"), false, 1,
            (translate("Ate"), "✓", EventStatusSemantic.Positive),
            (translate("DidNotEat"), "×", EventStatusSemantic.Negative));
    }

    private static async Task EnsureBuiltInEventAsync(
        SQLiteAsyncConnection db,
        int organizationId,
        string systemKey,
        string name,
        bool useExpectedRoster,
        int sortOrder,
        params (string Label, string Symbol, EventStatusSemantic Semantic)[] statuses)
    {
        var definition = await db.Table<EventDefinition>()
            .Where(e => e.OrganizationId == organizationId && e.SystemKey == systemKey)
            .FirstOrDefaultAsync();
        if (definition is null)
        {
            definition = new EventDefinition
            {
                OrganizationId = organizationId,
                Name = name,
                SystemKey = systemKey,
                UseExpectedRoster = useExpectedRoster,
                SortOrder = sortOrder
            };
            await db.InsertAsync(definition);
        }
        var existing = await db.Table<EventStatusOption>()
            .Where(o => o.EventDefinitionId == definition.Id)
            .ToListAsync();
        for (var index = 0; index < statuses.Length; index++)
        {
            if (existing.All(o => o.SortOrder != index))
            {
                var status = statuses[index];
                await db.InsertAsync(new EventStatusOption
                {
                    EventDefinitionId = definition.Id,
                    Label = status.Label,
                    Symbol = status.Symbol,
                    Semantic = status.Semantic,
                    SortOrder = index
                });
            }
        }
    }

    public async Task LocalizeBuiltInDefaultsAsync()
    {
        var db = await GetDatabaseAsync();
        await LocalizeUntouchedDefaultsAsync(db);
    }

    private static async Task LocalizeUntouchedDefaultsAsync(SQLiteAsyncConnection db)
    {
        var organizations = await db.Table<Organization>().ToListAsync();
        foreach (var organization in organizations.Where(value =>
                     LocalizationService.IsKnownTranslation("DefaultOrganization", value.Name)))
        {
            organization.Name = LocalizationService.T("DefaultOrganization");
            await db.UpdateAsync(organization);
        }

        var classes = await db.Table<ClassGroup>().ToListAsync();
        foreach (var classGroup in classes.Where(value =>
                     LocalizationService.IsKnownTranslation("DefaultClass", value.Name)))
        {
            classGroup.Name = LocalizationService.T("DefaultClass");
            await db.UpdateAsync(classGroup);
        }

        var events = await db.Table<EventDefinition>().ToListAsync();
        var options = await db.Table<EventStatusOption>().ToListAsync();
        foreach (var definition in events.Where(value => !string.IsNullOrWhiteSpace(value.SystemKey)))
        {
            var eventKey = definition.SystemKey switch
            {
                "attendance" => "Attendance",
                "meal" => "Meals",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(eventKey)
                && LocalizationService.IsKnownTranslation(eventKey, definition.Name))
            {
                var translatedName = LocalizationService.T(eventKey);
                var nameConflict = events.Any(other =>
                    other.Id != definition.Id
                    && other.OrganizationId == definition.OrganizationId
                    && string.Equals(other.Name, translatedName, StringComparison.Ordinal));
                if (!nameConflict)
                {
                    definition.Name = translatedName;
                    await db.UpdateAsync(definition);
                }
            }

            var statusKeys = definition.SystemKey switch
            {
                "attendance" => new[] { "Present", "Absent", "Excused" },
                "meal" => new[] { "Ate", "DidNotEat" },
                _ => []
            };
            foreach (var option in options.Where(value => value.EventDefinitionId == definition.Id))
            {
                if (option.SortOrder < 0 || option.SortOrder >= statusKeys.Length)
                {
                    continue;
                }
                var statusKey = statusKeys[option.SortOrder];
                if (LocalizationService.IsKnownTranslation(statusKey, option.Label))
                {
                    option.Label = LocalizationService.T(statusKey);
                    await db.UpdateAsync(option);
                }
            }
        }
    }

    private static async Task EnsureClassEventAsync(
        SQLiteAsyncConnection db,
        int classGroupId,
        int eventDefinitionId,
        int sortOrder,
        bool enabled)
    {
        var value = await db.Table<ClassEvent>()
            .Where(e => e.ClassGroupId == classGroupId && e.EventDefinitionId == eventDefinitionId)
            .FirstOrDefaultAsync();
        if (value is null)
        {
            await db.InsertAsync(new ClassEvent
            {
                ClassGroupId = classGroupId,
                EventDefinitionId = eventDefinitionId,
                SortOrder = sortOrder,
                IsEnabled = enabled
            });
        }
    }

    public async Task<(Organization Organization, ClassGroup ClassGroup)> GetDefaultContextAsync()
    {
        var db = await GetDatabaseAsync();
        var organization = await db.Table<Organization>()
            .Where(o => !o.IsArchived)
            .OrderBy(o => o.Id)
            .FirstAsync();
        var classGroup = await db.Table<ClassGroup>()
            .Where(c => c.OrganizationId == organization.Id && !c.IsArchived)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
        if (classGroup is null)
        {
            classGroup = await AddClassAsync(organization.Id, LocalizationService.T("DefaultClass"));
        }
        return (organization, classGroup);
    }

    public async Task<IReadOnlyList<Organization>> GetOrganizationsAsync(bool includeArchived = false)
    {
        var db = await GetDatabaseAsync();
        var rows = await db.Table<Organization>().OrderBy(o => o.Name).ToListAsync();
        return includeArchived ? rows : rows.Where(o => !o.IsArchived).ToList();
    }

    public async Task<Organization> AddOrganizationAsync(string name)
    {
        var clean = RequiredName(name);
        var db = await GetDatabaseAsync();
        var organization = new Organization { Name = clean };
        await db.InsertAsync(organization);
        await EnsureOrganizationEventsAsync(db, organization.Id, _translate);
        await AddClassAsync(organization.Id, LocalizationService.T("DefaultClass"));
        return organization;
    }

    public async Task UpdateOrganizationNameAsync(Organization organization, string name)
    {
        organization.Name = RequiredName(name);
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(organization);
    }

    public async Task<IReadOnlyList<ClassGroup>> GetClassesAsync(int organizationId, bool includeArchived = false)
    {
        var db = await GetDatabaseAsync();
        var rows = await db.Table<ClassGroup>()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return includeArchived ? rows : rows.Where(c => !c.IsArchived).ToList();
    }

    public async Task<ClassGroup> GetClassAsync(int classGroupId)
    {
        var db = await GetDatabaseAsync();
        return await db.GetAsync<ClassGroup>(classGroupId);
    }

    public async Task<Organization> GetOrganizationAsync(int organizationId)
    {
        var db = await GetDatabaseAsync();
        return await db.GetAsync<Organization>(organizationId);
    }

    public async Task<ClassGroup> AddClassAsync(int organizationId, string name)
    {
        var clean = RequiredName(name);
        var db = await GetDatabaseAsync();
        var classGroup = new ClassGroup
        {
            OrganizationId = organizationId,
            Name = clean,
            OperatingDaysMask = 127
        };
        await db.InsertAsync(classGroup);
        await EnsureOrganizationEventsAsync(db, organizationId, _translate);
        var builtIns = await db.Table<EventDefinition>()
            .Where(e => e.OrganizationId == organizationId && !e.IsArchived)
            .ToListAsync();
        foreach (var definition in builtIns.Where(e => !string.IsNullOrWhiteSpace(e.SystemKey)))
        {
            await EnsureClassEventAsync(db, classGroup.Id, definition.Id, definition.SortOrder, true);
        }
        return classGroup;
    }

    public async Task UpdateClassAsync(ClassGroup classGroup)
    {
        classGroup.Name = RequiredName(classGroup.Name);
        classGroup.SubjectLabel = classGroup.SubjectLabel?.Trim() ?? string.Empty;
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(classGroup);
    }

    public async Task SetClassArchivedAsync(ClassGroup classGroup, bool archived)
    {
        classGroup.IsArchived = archived;
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(classGroup);
    }

    public async Task<IReadOnlyList<ClassMember>> GetClassMembersAsync(int classGroupId, bool includeArchived = false)
    {
        var db = await GetDatabaseAsync();
        var memberships = await db.Table<ClassMembership>()
            .Where(m => m.ClassGroupId == classGroupId)
            .ToListAsync();
        var people = await db.QueryAsync<Person>(
            "SELECT p.* FROM People p INNER JOIN ClassMemberships m ON m.PersonId = p.Id " +
            "WHERE m.ClassGroupId = ?",
            classGroupId);
        var peopleById = people.ToDictionary(p => p.Id);
        return memberships
            .Where(m => includeArchived || m.IsActive)
            .Where(m => peopleById.ContainsKey(m.PersonId))
            .Select(m => new ClassMember { Membership = m, Person = peopleById[m.PersonId] })
            .OrderBy(m => m.Membership.SortOrder)
            .ThenBy(m => m.DisplayName)
            .ToList();
    }

    public async Task<ClassMember> AddPersonToClassAsync(int classGroupId, string displayName)
    {
        var clean = RequiredName(displayName);
        var db = await GetDatabaseAsync();
        var nextPersonOrder = await db.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM People");
        var person = new Person { DisplayName = clean, SortOrder = nextPersonOrder };
        await db.InsertAsync(person);

        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        var nextMemberOrder = await db.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ClassMemberships WHERE ClassGroupId = ?",
            classGroupId);
        var membership = new ClassMembership
        {
            ClassGroupId = classGroupId,
            PersonId = person.Id,
            SortOrder = nextMemberOrder,
            ExpectedDaysMask = classGroup.OperatingDaysMask
        };
        await db.InsertAsync(membership);
        await db.InsertAsync(new ClassMembershipPeriod
        {
            ClassMembershipId = membership.Id,
            StartDateKey = membership.StartDateKey
        });
        return new ClassMember { Person = person, Membership = membership };
    }

    public async Task UpdateMemberNameAsync(ClassMember member, string name)
    {
        member.Person.DisplayName = RequiredName(name);
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(member.Person);
    }

    public Task SetMembershipActiveAsync(ClassMembership membership, bool active) =>
        SetMembershipActiveAsync(membership, active, DateTime.Today);

    internal async Task SetMembershipActiveAsync(
        ClassMembership membership,
        bool active,
        DateTime effectiveDate)
    {
        if (membership.IsActive == active)
        {
            return;
        }

        var today = DateKey(effectiveDate);
        membership.IsActive = active;
        membership.StartDateKey = active ? today : membership.StartDateKey;
        membership.EndDateKey = active ? null : today;
        var db = await GetDatabaseAsync();
        await db.RunInTransactionAsync(connection =>
        {
            connection.Update(membership);
            if (active)
            {
                var period = connection.Table<ClassMembershipPeriod>()
                    .FirstOrDefault(value =>
                        value.ClassMembershipId == membership.Id
                        && value.StartDateKey == today);
                if (period is null)
                {
                    connection.Insert(new ClassMembershipPeriod
                    {
                        ClassMembershipId = membership.Id,
                        StartDateKey = today
                    });
                }
                else
                {
                    period.EndDateKey = null;
                    connection.Update(period);
                }
            }
            else
            {
                var period = connection.Table<ClassMembershipPeriod>()
                    .Where(value => value.ClassMembershipId == membership.Id)
                    .OrderByDescending(value => value.StartDateKey)
                    .FirstOrDefault(value => string.IsNullOrEmpty(value.EndDateKey));
                if (period is not null)
                {
                    period.EndDateKey = today;
                    connection.Update(period);
                }
            }
        });
    }

    public async Task UpdateMembershipScheduleAsync(ClassMembership membership, int expectedDaysMask)
    {
        membership.ExpectedDaysMask = expectedDaysMask;
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(membership);
    }

    public async Task SaveDayOverrideAsync(int classGroupId, DateTime date, DayOverrideKind kind, string label)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var value = await db.Table<DayOverride>()
            .Where(o => o.ClassGroupId == classGroupId && o.DateKey == dateKey)
            .FirstOrDefaultAsync();
        if (value is null)
        {
            value = new DayOverride { ClassGroupId = classGroupId, DateKey = dateKey };
        }
        value.Kind = kind;
        value.Label = label.Trim();
        if (value.Id == 0)
        {
            await db.InsertAsync(value);
        }
        else
        {
            await db.UpdateAsync(value);
        }
    }

    public async Task DeleteDayOverrideAsync(int classGroupId, DateTime date)
    {
        var db = await GetDatabaseAsync();
        await db.ExecuteAsync(
            "DELETE FROM DayOverrides WHERE ClassGroupId = ? AND DateKey = ?",
            classGroupId,
            DateKey(date));
    }

    public async Task<IReadOnlyList<DayOverride>> GetDayOverridesAsync(int classGroupId, DateTime start, DateTime end)
    {
        var db = await GetDatabaseAsync();
        return await db.QueryAsync<DayOverride>(
            "SELECT * FROM DayOverrides WHERE ClassGroupId = ? AND DateKey >= ? AND DateKey <= ? ORDER BY DateKey",
            classGroupId,
            DateKey(start),
            DateKey(end));
    }

    public async Task<ClassDay> GetOrCreateClassDayAsync(int classGroupId, DateTime date)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        var organization = await db.GetAsync<Organization>(classGroup.OrganizationId);
        var now = DateTimeOffset.Now.ToString("O");
        await db.ExecuteAsync(
            "INSERT OR IGNORE INTO ClassDays " +
            "(ClassGroupId, DateKey, OrganizationNameSnapshot, ClassNameSnapshot, SubjectLabelSnapshot, " +
            "RosterSource, IsBackfill, RosterConfirmedAtIso, CreatedAtIso) VALUES (?, ?, ?, ?, ?, ?, 0, ?, ?)",
            classGroupId,
            dateKey,
            organization.Name,
            classGroup.Name,
            classGroup.SubjectLabel,
            (int)RosterSource.HistoricalMembership,
            now,
            now);
        var day = await db.Table<ClassDay>()
            .Where(d => d.ClassGroupId == classGroupId && d.DateKey == dateKey)
            .FirstAsync();
        await EnsureClassDayPeopleAsync(db, day, date.Date);
        return day;
    }

    public async Task<IReadOnlyList<BackfillRosterCandidate>> GetBackfillRosterCandidatesAsync(
        int classGroupId,
        DateTime date)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        var dayOverride = await db.Table<DayOverride>()
            .Where(value => value.ClassGroupId == classGroupId && value.DateKey == dateKey)
            .FirstOrDefaultAsync();
        var operating = dayOverride?.Kind switch
        {
            DayOverrideKind.Excluded => false,
            DayOverrideKind.Included or DayOverrideKind.Special => true,
            _ => HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
        };
        var memberships = await db.Table<ClassMembership>()
            .Where(value => value.ClassGroupId == classGroupId)
            .ToListAsync();
        var periods = (await QueryByIdsAsync<ClassMembershipPeriod>(
                db,
                "ClassMembershipPeriods",
                "ClassMembershipId",
                memberships.Select(value => value.Id).ToHashSet()))
            .GroupBy(value => value.ClassMembershipId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var people = (await QueryByIdsAsync<Person>(
                db,
                "People",
                "Id",
                memberships.Select(value => value.PersonId).ToHashSet()))
            .ToDictionary(value => value.Id);

        return memberships
            .Where(value => people.ContainsKey(value.PersonId))
            .Select(value => new BackfillRosterCandidate
            {
                MembershipId = value.Id,
                PersonId = value.PersonId,
                Name = people[value.PersonId].DisplayName,
                SortOrder = value.SortOrder,
                WasMemberOnDate = MembershipRules.IncludesDate(
                    value,
                    periods.GetValueOrDefault(value.Id) ?? [],
                    dateKey),
                IsCurrentMember = value.IsActive,
                IsExpectedOnDate = operating && HasDay(value.ExpectedDaysMask, date.DayOfWeek)
            })
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .ToList();
    }

    public async Task<ClassDay> SaveBackfillRosterAsync(
        int classGroupId,
        DateTime date,
        IReadOnlyList<BackfillRosterSelection> selections,
        RosterSource source)
    {
        var cleanSelections = selections
            .Where(value => value.MembershipId > 0 || !string.IsNullOrWhiteSpace(value.HistoricalOnlyName))
            .GroupBy(value => value.MembershipId > 0
                ? $"membership:{value.MembershipId}"
                : $"name:{value.HistoricalOnlyName.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (cleanSelections.Count == 0)
        {
            throw new InvalidOperationException(_translate("BackfillSelectPersonRequired"));
        }
        if (source is RosterSource.Unspecified or RosterSource.LegacySnapshot)
        {
            source = RosterSource.ManualSelection;
        }

        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var now = DateTimeOffset.Now.ToString("O");
        var alreadyExistsMessage = _translate("BackfillRosterAlreadyExists");
        ClassDay? savedDay = null;
        await db.RunInTransactionAsync(connection =>
        {
            var classGroup = connection.Get<ClassGroup>(classGroupId);
            var organization = connection.Get<Organization>(classGroup.OrganizationId);
            var day = connection.Table<ClassDay>()
                .FirstOrDefault(value => value.ClassGroupId == classGroupId && value.DateKey == dateKey);
            if (day is null)
            {
                day = new ClassDay
                {
                    ClassGroupId = classGroupId,
                    DateKey = dateKey,
                    CreatedAtIso = now
                };
                connection.Insert(day);
            }
            else if (connection.Table<ClassDayPerson>().Any(value => value.ClassDayId == day.Id))
            {
                throw new InvalidOperationException(alreadyExistsMessage);
            }

            day.OrganizationNameSnapshot = organization.Name;
            day.ClassNameSnapshot = classGroup.Name;
            day.SubjectLabelSnapshot = classGroup.SubjectLabel;
            day.RosterSource = source;
            day.IsBackfill = true;
            day.RosterConfirmedAtIso = now;
            connection.Update(day);

            var nextPersonOrder = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM People");
            var nextMembershipOrder = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ClassMemberships WHERE ClassGroupId = ?",
                classGroupId);
            var dayPeople = new List<ClassDayPerson>();
            foreach (var selection in cleanSelections)
            {
                Person person;
                ClassMembership membership;
                if (selection.MembershipId > 0)
                {
                    membership = connection.Get<ClassMembership>(selection.MembershipId);
                    if (membership.ClassGroupId != classGroupId)
                    {
                        throw new InvalidOperationException(alreadyExistsMessage);
                    }
                    person = connection.Get<Person>(membership.PersonId);
                }
                else
                {
                    var name = RequiredName(selection.HistoricalOnlyName);
                    person = new Person
                    {
                        DisplayName = name,
                        IsActive = false,
                        SortOrder = nextPersonOrder++,
                        ArchivedAtIso = now,
                        CreatedAtIso = now
                    };
                    connection.Insert(person);
                    membership = new ClassMembership
                    {
                        ClassGroupId = classGroupId,
                        PersonId = person.Id,
                        IsActive = false,
                        SortOrder = nextMembershipOrder++,
                        ExpectedDaysMask = classGroup.OperatingDaysMask,
                        StartDateKey = dateKey,
                        EndDateKey = dateKey
                    };
                    connection.Insert(membership);
                    connection.Insert(new ClassMembershipPeriod
                    {
                        ClassMembershipId = membership.Id,
                        StartDateKey = dateKey,
                        EndDateKey = dateKey
                    });
                }

                var dayPerson = new ClassDayPerson
                {
                    ClassDayId = day.Id,
                    PersonId = person.Id,
                    DisplayNameSnapshot = person.DisplayName,
                    IsExpected = selection.IsExpected,
                    SortOrderSnapshot = membership.SortOrder,
                    Note = string.Empty
                };
                connection.Insert(dayPerson);
                dayPeople.Add(dayPerson);
            }

            var enabledEvents = connection.Query<EventDefinition>(
                "SELECT d.* FROM EventDefinitions d INNER JOIN ClassEvents ce " +
                "ON ce.EventDefinitionId = d.Id " +
                "WHERE ce.ClassGroupId = ? AND ce.IsEnabled = 1 AND d.IsArchived = 0 " +
                "ORDER BY ce.SortOrder, d.SortOrder",
                classGroupId);
            foreach (var dayPerson in dayPeople)
            {
                foreach (var definition in enabledEvents)
                {
                    connection.Insert(new EventRecord
                    {
                        ClassDayPersonId = dayPerson.Id,
                        EventDefinitionId = definition.Id,
                        RecordedAtIso = now,
                        UpdatedAtIso = now
                    });
                }
                foreach (var kind in new[] { TrackingKind.Attendance, TrackingKind.Meal })
                {
                    connection.Insert(new TrackingRecord
                    {
                        ClassDayPersonId = dayPerson.Id,
                        Kind = kind,
                        RecordedAtIso = now,
                        UpdatedAtIso = now
                    });
                }
            }
            savedDay = day;
        });
        return savedDay!;
    }

    private static async Task EnsureClassDayPeopleAsync(
        SQLiteAsyncConnection db,
        ClassDay day,
        DateTime date)
    {
        var classGroup = await db.GetAsync<ClassGroup>(day.ClassGroupId);
        var dayOverride = await db.Table<DayOverride>()
            .Where(o => o.ClassGroupId == day.ClassGroupId && o.DateKey == day.DateKey)
            .FirstOrDefaultAsync();
        var operating = dayOverride?.Kind switch
        {
            DayOverrideKind.Excluded => false,
            DayOverrideKind.Included or DayOverrideKind.Special => true,
            _ => HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
        };

        var memberships = await db.Table<ClassMembership>()
            .Where(m => m.ClassGroupId == day.ClassGroupId)
            .ToListAsync();
        var membershipIds = memberships.Select(value => value.Id).ToHashSet();
        var periods = (await QueryByIdsAsync<ClassMembershipPeriod>(
                db,
                "ClassMembershipPeriods",
                "ClassMembershipId",
                membershipIds))
            .GroupBy(period => period.ClassMembershipId)
            .ToDictionary(group => group.Key, group => group.ToList());
        memberships = memberships.Where(membership => MembershipRules.IncludesDate(
                membership,
                periods.GetValueOrDefault(membership.Id) ?? [],
                day.DateKey))
            .ToList();
        var existing = await db.Table<ClassDayPerson>()
            .Where(p => p.ClassDayId == day.Id)
            .ToListAsync();
        var people = await QueryByIdsAsync<Person>(
            db,
            "People",
            "Id",
            memberships.Select(value => value.PersonId).ToHashSet());
        var peopleById = people.ToDictionary(p => p.Id);
        var existingByPerson = existing.ToDictionary(p => p.PersonId);

        foreach (var membership in memberships.Where(m => peopleById.ContainsKey(m.PersonId)))
        {
            if (!existingByPerson.TryGetValue(membership.PersonId, out var dayPerson))
            {
                var person = peopleById[membership.PersonId];
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO ClassDayPeople " +
                    "(ClassDayId, PersonId, DisplayNameSnapshot, IsExpected, SortOrderSnapshot, Note) " +
                    "VALUES (?, ?, ?, ?, ?, ?)",
                    day.Id,
                    person.Id,
                    person.DisplayName,
                    operating && HasDay(membership.ExpectedDaysMask, date.DayOfWeek),
                    membership.SortOrder,
                    string.Empty);
                dayPerson = await db.Table<ClassDayPerson>()
                    .Where(value => value.ClassDayId == day.Id && value.PersonId == person.Id)
                    .FirstAsync();
                existingByPerson[person.Id] = dayPerson;
            }
        }

        await EnsureDaySnapshotRecordsAsync(db, day);
    }

    private static async Task EnsureDaySnapshotRecordsAsync(SQLiteAsyncConnection db, ClassDay day)
    {
        var dayPeople = await db.Table<ClassDayPerson>()
            .Where(value => value.ClassDayId == day.Id)
            .ToListAsync();
        var enabledEvents = await db.QueryAsync<EventDefinition>(
            "SELECT d.* FROM EventDefinitions d INNER JOIN ClassEvents ce ON ce.EventDefinitionId = d.Id " +
            "WHERE ce.ClassGroupId = ? AND ce.IsEnabled = 1 AND d.IsArchived = 0 ORDER BY ce.SortOrder, d.SortOrder",
            day.ClassGroupId);
        var existingEventRecords = await db.QueryAsync<EventRecord>(
            "SELECT r.* FROM EventRecords r INNER JOIN ClassDayPeople p ON p.Id = r.ClassDayPersonId " +
            "WHERE p.ClassDayId = ?",
            day.Id);
        var eventRecordKeys = existingEventRecords
            .Select(record => (record.ClassDayPersonId, record.EventDefinitionId))
            .ToHashSet();
        var existingTrackingRecords = await db.QueryAsync<TrackingRecord>(
            "SELECT r.* FROM TrackingRecords r INNER JOIN ClassDayPeople p ON p.Id = r.ClassDayPersonId " +
            "WHERE p.ClassDayId = ?",
            day.Id);
        var trackingRecordKeys = existingTrackingRecords
            .Select(record => (record.ClassDayPersonId, record.Kind))
            .ToHashSet();

        // Records follow the immutable day snapshot, not today's membership.
        // This also lets a newly enabled event be recorded against a manually
        // confirmed backfill roster without changing who belonged to the day.
        var now = DateTimeOffset.Now.ToString("O");
        foreach (var dayPerson in dayPeople)
        {
            foreach (var definition in enabledEvents)
            {
                if (eventRecordKeys.Add((dayPerson.Id, definition.Id)))
                {
                    await db.ExecuteAsync(
                        "INSERT OR IGNORE INTO EventRecords " +
                        "(ClassDayPersonId, EventDefinitionId, StatusOptionId, RecordedAtIso, UpdatedAtIso) " +
                        "VALUES (?, ?, 0, ?, ?)",
                        dayPerson.Id,
                        definition.Id,
                        now,
                        now);
                }
            }

            // Retained for old installations and compatibility with v2 exports.
            foreach (var kind in new[] { TrackingKind.Attendance, TrackingKind.Meal })
            {
                if (trackingRecordKeys.Add((dayPerson.Id, kind)))
                {
                    await db.ExecuteAsync(
                        "INSERT OR IGNORE INTO TrackingRecords " +
                        "(ClassDayPersonId, Kind, Status, RecordedAtIso, UpdatedAtIso) " +
                        "VALUES (?, ?, 0, ?, ?)",
                        dayPerson.Id,
                        (int)kind,
                        now,
                        now);
                }
            }
        }
    }

    public async Task<IReadOnlyList<DailyEventItem>> GetDailyEventPreviewAsync(
        int classGroupId,
        DateTime date,
        int eventDefinitionId,
        bool activeMembersOnly = false)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        var dayOverride = await db.Table<DayOverride>()
            .Where(value => value.ClassGroupId == classGroupId && value.DateKey == dateKey)
            .FirstOrDefaultAsync();
        var operating = dayOverride?.Kind switch
        {
            DayOverrideKind.Excluded => false,
            DayOverrideKind.Included or DayOverrideKind.Special => true,
            _ => HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
        };
        var memberships = await db.Table<ClassMembership>()
            .Where(value => value.ClassGroupId == classGroupId)
            .ToListAsync();
        if (activeMembersOnly)
        {
            memberships = memberships.Where(value => value.IsActive).ToList();
        }
        var membershipIds = memberships.Select(value => value.Id).ToHashSet();
        var periods = (await QueryByIdsAsync<ClassMembershipPeriod>(
                db,
                "ClassMembershipPeriods",
                "ClassMembershipId",
                membershipIds))
            .GroupBy(period => period.ClassMembershipId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var people = (await QueryByIdsAsync<Person>(
                db,
                "People",
                "Id",
                memberships.Select(value => value.PersonId).ToHashSet()))
            .ToDictionary(value => value.Id);
        return memberships
            .Where(membership => people.ContainsKey(membership.PersonId))
            .Where(membership => MembershipRules.IncludesDate(
                membership,
                periods.GetValueOrDefault(membership.Id) ?? [],
                dateKey))
            .Select(membership => new DailyEventItem
            {
                DayPerson = new ClassDayPerson
                {
                    PersonId = membership.PersonId,
                    DisplayNameSnapshot = people[membership.PersonId].DisplayName,
                    IsExpected = operating && HasDay(membership.ExpectedDaysMask, date.DayOfWeek),
                    SortOrderSnapshot = membership.SortOrder
                },
                Record = new EventRecord { EventDefinitionId = eventDefinitionId }
            })
            .OrderBy(item => item.DayPerson.SortOrderSnapshot)
            .ThenBy(item => item.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<ClassEventDefinition>> GetEventsForClassAsync(
        int classGroupId,
        bool includeDisabled = false)
    {
        var db = await GetDatabaseAsync();
        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        await EnsureOrganizationEventsAsync(db, classGroup.OrganizationId, _translate);
        var definitions = await db.Table<EventDefinition>()
            .Where(e => e.OrganizationId == classGroup.OrganizationId && !e.IsArchived)
            .ToListAsync();
        var links = await db.Table<ClassEvent>()
            .Where(e => e.ClassGroupId == classGroupId)
            .ToListAsync();
        var options = await QueryByIdsAsync<EventStatusOption>(
            db,
            "EventStatusOptions",
            "EventDefinitionId",
            definitions.Select(value => value.Id).ToHashSet());
        return definitions
            .Select(definition => new ClassEventDefinition
            {
                Event = definition,
                ClassEvent = links.FirstOrDefault(link => link.EventDefinitionId == definition.Id),
                StatusOptions = options
                    .Where(option => option.EventDefinitionId == definition.Id)
                    .OrderBy(option => option.SortOrder)
                    .ToList()
            })
            .Where(value => includeDisabled || value.IsEnabled)
            .OrderBy(value => value.ClassEvent?.SortOrder ?? int.MaxValue)
            .ThenBy(value => value.Event.SortOrder)
            .ThenBy(value => value.Name)
            .ToList();
    }

    public async Task<EventDefinition> GetEventAsync(int eventDefinitionId)
    {
        var db = await GetDatabaseAsync();
        return await db.GetAsync<EventDefinition>(eventDefinitionId);
    }

    public async Task<IReadOnlyList<EventStatusOption>> GetEventStatusOptionsAsync(int eventDefinitionId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<EventStatusOption>()
            .Where(o => o.EventDefinitionId == eventDefinitionId)
            .OrderBy(o => o.SortOrder)
            .ToListAsync();
    }

    public async Task<EventDefinition> AddEventAsync(
        int organizationId,
        int classGroupId,
        string name,
        string positiveLabel,
        string negativeLabel)
    {
        var db = await GetDatabaseAsync();
        var nextOrder = await db.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM EventDefinitions WHERE OrganizationId = ?",
            organizationId);
        var definition = new EventDefinition
        {
            OrganizationId = organizationId,
            Name = RequiredName(name),
            SortOrder = nextOrder
        };
        await db.InsertAsync(definition);
        await db.InsertAllAsync(new[]
        {
            new EventStatusOption
            {
                EventDefinitionId = definition.Id,
                Label = RequiredName(positiveLabel),
                Symbol = "✓",
                Semantic = EventStatusSemantic.Positive,
                SortOrder = 0
            },
            new EventStatusOption
            {
                EventDefinitionId = definition.Id,
                Label = RequiredName(negativeLabel),
                Symbol = "×",
                Semantic = EventStatusSemantic.Negative,
                SortOrder = 1
            }
        });
        await EnsureClassEventAsync(db, classGroupId, definition.Id, nextOrder, true);
        return definition;
    }

    public async Task UpdateEventAsync(EventDefinition definition)
    {
        definition.Name = RequiredName(definition.Name);
        definition.PositiveSymbol = string.IsNullOrWhiteSpace(definition.PositiveSymbol)
            ? "✓"
            : definition.PositiveSymbol.Trim();
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(definition);
    }

    public async Task SetClassEventEnabledAsync(int classGroupId, int eventDefinitionId, bool enabled)
    {
        var db = await GetDatabaseAsync();
        var definition = await db.GetAsync<EventDefinition>(eventDefinitionId);
        await EnsureClassEventAsync(db, classGroupId, eventDefinitionId, definition.SortOrder, enabled);
        var link = await db.Table<ClassEvent>()
            .Where(e => e.ClassGroupId == classGroupId && e.EventDefinitionId == eventDefinitionId)
            .FirstAsync();
        link.IsEnabled = enabled;
        await db.UpdateAsync(link);
    }

    public async Task<EventStatusOption> AddStatusOptionAsync(
        int eventDefinitionId,
        string label,
        string symbol,
        EventStatusSemantic semantic)
    {
        var db = await GetDatabaseAsync();
        var nextOrder = await db.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM EventStatusOptions WHERE EventDefinitionId = ?",
            eventDefinitionId);
        var option = new EventStatusOption
        {
            EventDefinitionId = eventDefinitionId,
            Label = RequiredName(label),
            Symbol = string.IsNullOrWhiteSpace(symbol) ? "•" : symbol.Trim(),
            Semantic = semantic,
            SortOrder = nextOrder
        };
        await db.InsertAsync(option);
        return option;
    }

    public async Task UpdateStatusOptionAsync(EventStatusOption option)
    {
        option.Label = RequiredName(option.Label);
        option.Symbol = string.IsNullOrWhiteSpace(option.Symbol) ? "•" : option.Symbol.Trim();
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(option);
    }

    public async Task<IReadOnlyList<DailyEventItem>> GetDailyEventItemsAsync(
        int classDayId,
        int eventDefinitionId,
        bool activeMembersOnly = false)
    {
        var db = await GetDatabaseAsync();
        var day = await db.GetAsync<ClassDay>(classDayId);
        var date = DateTime.ParseExact(
            day.DateKey,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
        if (!day.IsBackfill && date.Date >= DateTime.Today)
        {
            await EnsureClassDayPeopleAsync(db, day, date);
        }
        else
        {
            await EnsureDaySnapshotRecordsAsync(db, day);
        }
        var people = await db.Table<ClassDayPerson>()
            .Where(p => p.ClassDayId == classDayId)
            .ToListAsync();
        if (activeMembersOnly)
        {
            var activePersonIds = (await db.Table<ClassMembership>()
                    .Where(m => m.ClassGroupId == day.ClassGroupId && m.IsActive)
                    .ToListAsync())
                .Select(m => m.PersonId)
                .ToHashSet();
            people = people.Where(p => activePersonIds.Contains(p.PersonId)).ToList();
        }
        var records = (await db.QueryAsync<EventRecord>(
                "SELECT r.* FROM EventRecords r " +
                "INNER JOIN ClassDayPeople p ON p.Id = r.ClassDayPersonId " +
                "WHERE p.ClassDayId = ? AND r.EventDefinitionId = ?",
                classDayId,
                eventDefinitionId))
            .ToDictionary(r => r.ClassDayPersonId);
        return people
            .Where(p => records.ContainsKey(p.Id))
            .Select(p => new DailyEventItem { DayPerson = p, Record = records[p.Id] })
            .OrderBy(i => i.DayPerson.SortOrderSnapshot)
            .ThenBy(i => i.Name)
            .ToList();
    }

    public async Task SetEventStatusAsync(int recordId, int statusOptionId)
    {
        var db = await GetDatabaseAsync();
        var record = await db.GetAsync<EventRecord>(recordId);
        if (statusOptionId != 0)
        {
            var option = await db.GetAsync<EventStatusOption>(statusOptionId);
            if (option.EventDefinitionId != record.EventDefinitionId)
            {
                throw new InvalidOperationException(
                    LocalizationService.T("StatusEventMismatchMessage"));
            }
        }
        record.StatusOptionId = statusOptionId;
        record.UpdatedAtIso = DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(record);
    }

    public async Task<EventCompletion?> GetEventCompletionAsync(int classDayId, int eventDefinitionId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<EventCompletion>()
            .Where(c => c.ClassDayId == classDayId && c.EventDefinitionId == eventDefinitionId)
            .FirstOrDefaultAsync();
    }

    public async Task CompleteEventAsync(int classDayId, int eventDefinitionId)
    {
        var db = await GetDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT OR IGNORE INTO EventCompletions (ClassDayId, EventDefinitionId, CompletedAtIso) " +
            "VALUES (?, ?, ?)",
            classDayId,
            eventDefinitionId,
            DateTimeOffset.Now.ToString("O"));
    }

    public async Task ReopenEventAsync(int classDayId, int eventDefinitionId)
    {
        var db = await GetDatabaseAsync();
        await db.ExecuteAsync(
            "DELETE FROM EventCompletions WHERE ClassDayId = ? AND EventDefinitionId = ?",
            classDayId,
            eventDefinitionId);
    }

    public async Task<IReadOnlyList<EventRecord>> GetEventRecordsAsync(
        IEnumerable<int> classDayPersonIds,
        int? eventDefinitionId = null)
    {
        var db = await GetDatabaseAsync();
        var ids = classDayPersonIds.ToHashSet();
        return await QueryByIdsAsync<EventRecord>(
            db,
            "EventRecords",
            "ClassDayPersonId",
            ids,
            eventDefinitionId is null ? string.Empty : " AND EventDefinitionId = ?",
            eventDefinitionId is null ? [] : [eventDefinitionId.Value]);
    }

    public async Task<IReadOnlyList<EventCompletion>> GetEventCompletionsAsync(IEnumerable<int> classDayIds)
    {
        var db = await GetDatabaseAsync();
        var ids = classDayIds.ToHashSet();
        return await QueryByIdsAsync<EventCompletion>(
            db,
            "EventCompletions",
            "ClassDayId",
            ids);
    }

    public async Task<IReadOnlyList<ClassMembershipPeriod>> GetMembershipPeriodsAsync(
        IEnumerable<int> classMembershipIds)
    {
        var db = await GetDatabaseAsync();
        return await QueryByIdsAsync<ClassMembershipPeriod>(
            db,
            "ClassMembershipPeriods",
            "ClassMembershipId",
            classMembershipIds.ToHashSet());
    }

    public async Task<bool> IsReminderContextActiveAsync(EventReminder reminder)
    {
        var db = await GetDatabaseAsync();
        var count = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ClassGroups c " +
            "INNER JOIN ClassEvents ce ON ce.ClassGroupId = c.Id " +
            "INNER JOIN EventDefinitions e ON e.Id = ce.EventDefinitionId " +
            "WHERE c.Id = ? AND e.Id = ? AND c.IsArchived = 0 AND e.IsArchived = 0 " +
            "AND ce.IsEnabled = 1 AND c.OrganizationId = e.OrganizationId",
            reminder.ClassGroupId,
            reminder.EventDefinitionId);
        return count > 0;
    }

    public async Task<IReadOnlyList<EventReminder>> GetEventRemindersAsync(
        int? classGroupId = null,
        int? eventDefinitionId = null)
    {
        var db = await GetDatabaseAsync();
        var reminders = await db.Table<EventReminder>().ToListAsync();
        return reminders
            .Where(r => classGroupId is null || r.ClassGroupId == classGroupId)
            .Where(r => eventDefinitionId is null || r.EventDefinitionId == eventDefinitionId)
            .OrderBy(r => r.TimeMinutes)
            .ToList();
    }

    public async Task SaveEventReminderAsync(EventReminder reminder)
    {
        var db = await GetDatabaseAsync();
        reminder.TimeMinutes = Math.Clamp(reminder.TimeMinutes, 0, 1439);
        reminder.Message = reminder.Message.Trim();
        if (reminder.Id == 0)
        {
            await db.InsertAsync(reminder);
        }
        else
        {
            await db.UpdateAsync(reminder);
        }
    }

    public async Task DeleteEventReminderAsync(EventReminder reminder)
    {
        var db = await GetDatabaseAsync();
        await db.DeleteAsync(reminder);
    }

    public async Task<bool> IsOperatingDayAsync(int classGroupId, DateTime date)
    {
        var db = await GetDatabaseAsync();
        var classGroup = await db.GetAsync<ClassGroup>(classGroupId);
        var dateKey = DateKey(date);
        var dayOverride = await db.Table<DayOverride>()
            .Where(o => o.ClassGroupId == classGroupId && o.DateKey == dateKey)
            .FirstOrDefaultAsync();
        return dayOverride?.Kind switch
        {
            DayOverrideKind.Excluded => false,
            DayOverrideKind.Included or DayOverrideKind.Special => true,
            _ => HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
        };
    }

    public async Task<IReadOnlyList<DailyTrackingItem>> GetDailyTrackingItemsAsync(int classDayId, TrackingKind kind)
    {
        var db = await GetDatabaseAsync();
        var people = await db.Table<ClassDayPerson>()
            .Where(p => p.ClassDayId == classDayId)
            .ToListAsync();
        var personIds = people.Select(p => p.Id).ToHashSet();
        var records = (await db.Table<TrackingRecord>().Where(r => r.Kind == kind).ToListAsync())
            .Where(r => personIds.Contains(r.ClassDayPersonId))
            .ToDictionary(r => r.ClassDayPersonId);
        return people
            .Where(p => records.ContainsKey(p.Id))
            .Select(p => new DailyTrackingItem { DayPerson = p, Record = records[p.Id] })
            .OrderBy(i => i.DayPerson.SortOrderSnapshot)
            .ThenBy(i => i.Name)
            .ToList();
    }

    public async Task SetTrackingStatusAsync(int recordId, int status)
    {
        var db = await GetDatabaseAsync();
        var record = await db.GetAsync<TrackingRecord>(recordId);
        record.Status = status;
        record.UpdatedAtIso = DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(record);
    }

    public async Task SetEventNoteAsync(int eventRecordId, string note)
    {
        var db = await GetDatabaseAsync();
        var record = await db.GetAsync<EventRecord>(eventRecordId);
        record.Note = note.Trim();
        record.UpdatedAtIso = DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(record);
    }

    public async Task<TrackingCompletion?> GetCompletionAsync(int classDayId, TrackingKind kind)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<TrackingCompletion>()
            .Where(c => c.ClassDayId == classDayId && c.Kind == kind)
            .FirstOrDefaultAsync();
    }

    public async Task CompleteTrackingAsync(int classDayId, TrackingKind kind)
    {
        var db = await GetDatabaseAsync();
        if (await GetCompletionAsync(classDayId, kind) is null)
        {
            await db.InsertAsync(new TrackingCompletion { ClassDayId = classDayId, Kind = kind });
        }
    }

    public async Task ReopenTrackingAsync(int classDayId, TrackingKind kind)
    {
        var db = await GetDatabaseAsync();
        await db.ExecuteAsync(
            "DELETE FROM TrackingCompletions WHERE ClassDayId = ? AND Kind = ?",
            classDayId,
            (int)kind);
    }

    public async Task<ClassDay?> GetClassDayAsync(int classGroupId, DateTime date)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        return await db.Table<ClassDay>()
            .Where(d => d.ClassGroupId == classGroupId && d.DateKey == dateKey)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<ClassDay>> GetClassDaysAsync(int classGroupId, DateTime start, DateTime end)
    {
        var db = await GetDatabaseAsync();
        return await db.QueryAsync<ClassDay>(
            "SELECT * FROM ClassDays WHERE ClassGroupId = ? AND DateKey >= ? AND DateKey <= ? ORDER BY DateKey",
            classGroupId,
            DateKey(start),
            DateKey(end));
    }

    public async Task<IReadOnlyList<ClassDay>> GetRecentClassDaysAsync(int classGroupId, int limit = 90)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<ClassDay>()
            .Where(d => d.ClassGroupId == classGroupId)
            .OrderByDescending(d => d.DateKey)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ClassDayPerson>> GetClassDayPeopleAsync(IEnumerable<int> classDayIds)
    {
        var db = await GetDatabaseAsync();
        var ids = classDayIds.ToHashSet();
        return await QueryByIdsAsync<ClassDayPerson>(db, "ClassDayPeople", "ClassDayId", ids);
    }

    public async Task<IReadOnlyList<TrackingRecord>> GetTrackingRecordsAsync(
        IEnumerable<int> classDayPersonIds,
        TrackingKind? kind = null)
    {
        var db = await GetDatabaseAsync();
        var ids = classDayPersonIds.ToHashSet();
        return await QueryByIdsAsync<TrackingRecord>(
            db,
            "TrackingRecords",
            "ClassDayPersonId",
            ids,
            kind is null ? string.Empty : " AND Kind = ?",
            kind is null ? [] : [(object)(int)kind.Value]);
    }

    public async Task<IReadOnlyList<TrackingCompletion>> GetCompletionsAsync(IEnumerable<int> classDayIds)
    {
        var db = await GetDatabaseAsync();
        var ids = classDayIds.ToHashSet();
        return await QueryByIdsAsync<TrackingCompletion>(db, "TrackingCompletions", "ClassDayId", ids);
    }

    public async Task<IReadOnlyList<ClassMembership>> GetMembershipsAsync(int classGroupId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<ClassMembership>()
            .Where(m => m.ClassGroupId == classGroupId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Person>> GetPeopleByIdsAsync(IEnumerable<int> ids)
    {
        var wanted = ids.ToHashSet();
        var db = await GetDatabaseAsync();
        return await QueryByIdsAsync<Person>(db, "People", "Id", wanted);
    }

    public async Task<IReadOnlyList<ExportProfile>> GetExportProfilesAsync(int classGroupId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<ExportProfile>()
            .Where(p => p.ClassGroupId == classGroupId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<ExportProfile> SaveExportProfileAsync(int classGroupId, ExportProfileDefinition definition)
    {
        var db = await GetDatabaseAsync();
        var cleanName = RequiredName(definition.Name);
        definition.Name = cleanName;
        var profile = await db.Table<ExportProfile>()
            .Where(p => p.ClassGroupId == classGroupId && p.Name == cleanName)
            .FirstOrDefaultAsync();
        profile ??= new ExportProfile { ClassGroupId = classGroupId, Name = cleanName };
        profile.DefinitionJson = JsonSerializer.Serialize(definition);
        profile.UpdatedAtIso = DateTimeOffset.Now.ToString("O");
        if (profile.Id == 0)
        {
            await db.InsertAsync(profile);
        }
        else
        {
            await db.UpdateAsync(profile);
        }
        return profile;
    }

    public async Task DeleteExportProfileAsync(ExportProfile profile)
    {
        var db = await GetDatabaseAsync();
        await db.DeleteAsync(profile);
    }

    public static ExportProfileDefinition ParseExportProfile(ExportProfile profile) =>
        JsonSerializer.Deserialize<ExportProfileDefinition>(profile.DefinitionJson) ?? new ExportProfileDefinition();

    public static bool HasDay(int mask, DayOfWeek day) => ScheduleRules.HasDay(mask, day);
    public static string DateKey(DateTime date) => date.Date.ToString("yyyy-MM-dd");

    private static string RequiredName(string value)
    {
        var clean = value.Trim();
        return string.IsNullOrWhiteSpace(clean)
            ? throw new ArgumentException(
                LocalizationService.T("NameRequiredMessage"),
                nameof(value))
            : clean;
    }

    private static async Task<IReadOnlyList<T>> QueryByIdsAsync<T>(
        SQLiteAsyncConnection db,
        string table,
        string column,
        IReadOnlyCollection<int> ids,
        string suffix = "",
        IReadOnlyList<object>? suffixArguments = null)
        where T : new()
    {
        if (ids.Count == 0)
        {
            return [];
        }

        suffixArguments ??= [];
        var result = new List<T>();
        foreach (var chunk in ids.Chunk(400))
        {
            var placeholders = string.Join(",", Enumerable.Repeat("?", chunk.Length));
            var arguments = chunk.Cast<object>().Concat(suffixArguments).ToArray();
            result.AddRange(await db.QueryAsync<T>(
                $"SELECT * FROM {table} WHERE {column} IN ({placeholders}){suffix}",
                arguments));
        }
        return result;
    }

    internal async Task CloseAsync()
    {
        var database = await GetDatabaseAsync();
        await database.CloseAsync();
    }

    // Legacy API retained while old installations are migrated and for compatibility.
    public async Task<IReadOnlyList<Person>> GetPeopleAsync(bool includeArchived = false)
    {
        var db = await GetDatabaseAsync();
        var people = await db.Table<Person>().ToListAsync();
        var ordered = people.OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName).ToList();
        return includeArchived ? ordered : ordered.Where(p => p.IsActive).ToList();
    }

    public async Task<Person> AddPersonAsync(string displayName)
    {
        var db = await GetDatabaseAsync();
        var nextOrder = await db.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM People");
        var person = new Person { DisplayName = RequiredName(displayName), SortOrder = nextOrder };
        await db.InsertAsync(person);
        return person;
    }

    public async Task SetPersonActiveAsync(Person person, bool isActive)
    {
        var db = await GetDatabaseAsync();
        person.IsActive = isActive;
        person.ArchivedAtIso = isActive ? null : DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(person);
    }

    public async Task UpdatePersonNameAsync(Person person, string displayName)
    {
        person.DisplayName = RequiredName(displayName);
        var db = await GetDatabaseAsync();
        await db.UpdateAsync(person);
    }

    public async Task<AttendanceSession> GetOrCreateSessionAsync(DateTime date)
    {
        var db = await GetDatabaseAsync();
        var dateKey = DateKey(date);
        var session = await db.Table<AttendanceSession>().Where(s => s.SessionDate == dateKey).FirstOrDefaultAsync();
        if (session is null)
        {
            session = new AttendanceSession { SessionDate = dateKey };
            await db.InsertAsync(session);
        }
        return session;
    }

    public async Task<IReadOnlyList<AttendanceItem>> GetAttendanceItemsAsync(int sessionId)
    {
        var db = await GetDatabaseAsync();
        var records = await db.Table<AttendanceRecord>().Where(r => r.SessionId == sessionId).ToListAsync();
        var people = (await db.Table<Person>().ToListAsync()).ToDictionary(p => p.Id);
        return records.Where(r => people.ContainsKey(r.PersonId))
            .Select(r => new AttendanceItem { Person = people[r.PersonId], Record = r }).ToList();
    }

    public async Task SetAttendanceStatusAsync(int recordId, AttendanceStatus status)
    {
        var db = await GetDatabaseAsync();
        var record = await db.GetAsync<AttendanceRecord>(recordId);
        record.Status = status;
        record.UpdatedAtIso = DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(record);
    }

    public async Task FinaliseSessionAsync(int sessionId)
    {
        var db = await GetDatabaseAsync();
        var session = await db.GetAsync<AttendanceSession>(sessionId);
        session.CompletedAtIso = DateTimeOffset.Now.ToString("O");
        await db.UpdateAsync(session);
    }

    public async Task ReopenSessionAsync(int sessionId)
    {
        var db = await GetDatabaseAsync();
        var session = await db.GetAsync<AttendanceSession>(sessionId);
        session.CompletedAtIso = null;
        await db.UpdateAsync(session);
    }

    public async Task<IReadOnlyList<AttendanceSession>> GetRecentSessionsAsync(int limit = 90)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<AttendanceSession>().OrderByDescending(s => s.SessionDate).Take(limit).ToListAsync();
    }

    public async Task<IReadOnlyList<AttendanceSession>> GetSessionsInRangeAsync(DateTime start, DateTime end)
    {
        var db = await GetDatabaseAsync();
        return await db.QueryAsync<AttendanceSession>(
            "SELECT * FROM AttendanceSessions WHERE SessionDate >= ? AND SessionDate <= ? ORDER BY SessionDate",
            DateKey(start), DateKey(end));
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetAllRecordsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<AttendanceRecord>().ToListAsync();
    }
}

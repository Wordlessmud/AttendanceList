using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CoreTests
{
    [TestMethod]
    public void DateFormattingUsesSelectedCultureRatherThanThreadCulture()
    {
        var date = new DateTime(2026, 8, 1);

        var chinese = LocalizationService.FormatDate(
            date,
            "D",
            CultureInfo.GetCultureInfo("zh-CN"));
        var english = LocalizationService.FormatDate(
            date,
            "D",
            CultureInfo.GetCultureInfo("en"));

        StringAssert.Contains(chinese, "2026\u5E748\u67081\u65E5");
        StringAssert.Contains(chinese, "\u661F\u671F\u516D");
        StringAssert.Contains(english, "August");
    }

    [ClassInitialize]
    public static void InitializeSqlite(TestContext _)
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        SQLitePCL.raw.FreezeProvider();
    }

    [TestInitialize]
    public void UseDeterministicTranslations()
    {
        LocalizationService.TranslationOverride = key => key;
        LocalizationService.CultureOverride = CultureInfo.GetCultureInfo("en");
    }

    [TestCleanup]
    public void RestoreTranslations()
    {
        LocalizationService.TranslationOverride = null;
        LocalizationService.CultureOverride = null;
    }

    [TestMethod]
    public async Task AsyncInitializationGateDoesNotPublishPartiallyInitializedValue()
    {
        var gate = new AsyncInitializationGate<object>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new object();
        var factoryCalls = 0;

        async Task<object> InitialiseAsync()
        {
            Interlocked.Increment(ref factoryCalls);
            started.SetResult();
            await release.Task;
            return expected;
        }

        var first = gate.GetAsync(InitialiseAsync);
        await started.Task;
        var second = gate.GetAsync(InitialiseAsync);

        Assert.IsFalse(second.IsCompleted, "A concurrent caller must wait for schema initialization.");
        release.SetResult();
        Assert.AreSame(expected, await first);
        Assert.AreSame(expected, await second);
        Assert.AreEqual(1, factoryCalls);
    }

    [TestMethod]
    public void TrackingSummariesKeepAttendanceAndMealRulesIndependent()
    {
        var attendance = TrackingRules.Summarise(
            new[] { 1, 1, 2, 3, 0 },
            TrackingKind.Attendance);
        var meals = TrackingRules.Summarise(
            new[] { 1, 2, 3, 0 },
            TrackingKind.Meal);

        Assert.AreEqual(new TrackingSummary(2, 1, 1, 1), attendance);
        Assert.AreEqual(new TrackingSummary(1, 1, 0, 1), meals);
    }

    [TestMethod]
    public void WeekdayMasksUseDayOfWeekBitPositions()
    {
        var mask = (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Saturday);

        Assert.IsTrue(ScheduleRules.HasDay(mask, DayOfWeek.Wednesday));
        Assert.IsTrue(ScheduleRules.HasDay(mask, DayOfWeek.Saturday));
        Assert.IsFalse(ScheduleRules.HasDay(mask, DayOfWeek.Monday));
    }

    [TestMethod]
    public void CustomEventSummaryKeepsUnmarkedSeparateFromNegative()
    {
        var summary = EventRules.Summarise(new EventStatusSemantic?[]
        {
            EventStatusSemantic.Positive,
            EventStatusSemantic.Negative,
            EventStatusSemantic.Neutral,
            EventStatusSemantic.Excused,
            null
        });

        Assert.AreEqual(1, summary.Positive);
        Assert.AreEqual(1, summary.Negative);
        Assert.AreEqual(1, summary.Neutral);
        Assert.AreEqual(1, summary.Excused);
        Assert.AreEqual(1, summary.Unknown);
    }

    [TestMethod]
    public void SummaryUsesTheRealStatusLabelWhenEachSemanticHasOneOption()
    {
        var options = new[]
        {
            new EventStatusOption { Label = "Sampled", Semantic = EventStatusSemantic.Positive },
            new EventStatusOption { Label = "Not sampled", Semantic = EventStatusSemantic.Negative }
        };

        var text = EventSummaryFormatter.Format(
            new EventSummary { Positive = 4, Negative = 1, Unknown = 2 },
            options,
            key => key);

        Assert.AreEqual("Sampled 4 · Not sampled 1 · UnmarkedGroup 2", text);
    }

    [TestMethod]
    public void SummaryUsesSemanticGroupNameWhenSeveralLabelsShareThatGroup()
    {
        var options = new[]
        {
            new EventStatusOption { Label = "Present", Semantic = EventStatusSemantic.Positive },
            new EventStatusOption { Label = "Late", Semantic = EventStatusSemantic.Positive },
            new EventStatusOption { Label = "Absent", Semantic = EventStatusSemantic.Negative }
        };

        var text = EventSummaryFormatter.Format(
            new EventSummary { Positive = 5, Negative = 1 },
            options,
            key => key);

        Assert.AreEqual("PositiveGroup 5 · Absent 1 · UnmarkedGroup 0", text);
    }

    [TestMethod]
    public void ReminderRulesRespectWeekdaysOperatingDaysAndCompletion()
    {
        var monday = new DateTime(2026, 8, 3);
        var mondayMask = 1 << (int)DayOfWeek.Monday;

        Assert.IsTrue(ReminderRules.ShouldSchedule(
            monday, mondayMask, "2026-08-01", null, true, true, true, false));
        Assert.IsFalse(ReminderRules.ShouldSchedule(
            monday, mondayMask, "2026-08-01", null, true, false, true, false));
        Assert.IsFalse(ReminderRules.ShouldSchedule(
            monday, mondayMask, "2026-08-01", null, true, true, true, true));
        Assert.IsFalse(ReminderRules.ShouldSchedule(
            monday.AddDays(1), mondayMask, "2026-08-01", null, false, true, false, false));
    }

    [TestMethod]
    public async Task FreshDatabaseCreatesEventSchemaWithoutWritingAViewedDay()
    {
        var directory = NewTemporaryDirectory();
        var path = Path.Combine(directory, "attendance.db3");
        var database = new DatabaseService(path);
        try
        {
            var context = await database.GetDefaultContextAsync();
            await database.AddPersonToClassAsync(context.ClassGroup.Id, "Preview person");
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");

            var preview = await database.GetDailyEventPreviewAsync(
                context.ClassGroup.Id,
                DateTime.Today,
                attendance.Event.Id,
                activeMembersOnly: true);

            Assert.HasCount(1, preview);
            Assert.AreEqual(0, preview[0].Record.Id);
            Assert.IsNull(await database.GetClassDayAsync(context.ClassGroup.Id, DateTime.Today));
            await database.CloseAsync();

            using var connection = new SQLite.SQLiteConnection(path);
            Assert.IsNotEmpty(connection.GetTableInfo("EventDefinitions"));
            Assert.IsNotEmpty(connection.GetTableInfo("EventRecords"));
            Assert.IsNotEmpty(connection.GetTableInfo("ClassMembershipPeriods"));
            var classDayColumns = connection.GetTableInfo("ClassDays").Select(value => value.Name).ToList();
            CollectionAssert.Contains(classDayColumns, "OrganizationNameSnapshot");
            CollectionAssert.Contains(classDayColumns, "ClassNameSnapshot");
            CollectionAssert.Contains(classDayColumns, "RosterSource");
            CollectionAssert.Contains(classDayColumns, "SubjectLabelSnapshot");
            var eventRecordColumns = connection.GetTableInfo("EventRecords").Select(value => value.Name).ToList();
            CollectionAssert.Contains(eventRecordColumns, "Note");
            Assert.AreEqual("7", connection.Find<AppMetadata>("SchemaVersion")?.Value);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task VersionThreeDatabaseMigratesMembershipPeriodsAfterTablesAreCreated()
    {
        var directory = NewTemporaryDirectory();
        var path = Path.Combine(directory, "attendance.db3");
        var membership = new ClassMembership();
        using (var legacy = new SQLite.SQLiteConnection(path))
        {
            legacy.CreateTable<Organization>();
            legacy.CreateTable<ClassGroup>();
            legacy.CreateTable<Person>();
            legacy.CreateTable<ClassMembership>();
            legacy.CreateTable<AppMetadata>();
            legacy.Execute(
                "CREATE TABLE ClassDays (" +
                "Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, " +
                "ClassGroupId INTEGER, DateKey VARCHAR(10), CreatedAtIso TEXT)");
            var organization = new Organization { Name = "Legacy organization" };
            legacy.Insert(organization);
            var classGroup = new ClassGroup { OrganizationId = organization.Id, Name = "Legacy class" };
            legacy.Insert(classGroup);
            var person = new Person { DisplayName = "Legacy person" };
            legacy.Insert(person);
            membership = new ClassMembership
            {
                ClassGroupId = classGroup.Id,
                PersonId = person.Id,
                StartDateKey = "2026-01-01",
                EndDateKey = "2026-02-01",
                IsActive = false
            };
            legacy.Insert(membership);
            legacy.Execute(
                "INSERT INTO ClassDays (ClassGroupId, DateKey, CreatedAtIso) VALUES (?, ?, ?)",
                classGroup.Id,
                "2026-01-15",
                DateTimeOffset.Now.ToString("O"));
            legacy.Insert(new AppMetadata { Key = "SchemaVersion", Value = "3" });
        }

        var database = new DatabaseService(path);
        try
        {
            await database.GetDefaultContextAsync();
            var periods = await database.GetMembershipPeriodsAsync([membership.Id]);
            Assert.HasCount(1, periods);
            Assert.AreEqual("2026-01-01", periods[0].StartDateKey);
            Assert.AreEqual("2026-02-01", periods[0].EndDateKey);
            var migratedDay = (await database.GetClassDaysAsync(
                membership.ClassGroupId,
                new DateTime(2026, 1, 15),
                new DateTime(2026, 1, 15))).Single();
            Assert.AreEqual("Legacy organization", migratedDay.OrganizationNameSnapshot);
            Assert.AreEqual("Legacy class", migratedDay.ClassNameSnapshot);

            await database.CloseAsync();
            using var connection = new SQLite.SQLiteConnection(path);
            Assert.AreEqual("7", connection.Find<AppMetadata>("SchemaVersion")?.Value);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task RestoringMembershipPreservesInactiveDateGap()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            var member = await database.AddPersonToClassAsync(context.ClassGroup.Id, "Returning person");
            var start = DateTime.Today;
            var archived = start.AddDays(1);
            var restored = start.AddDays(3);

            await database.SetMembershipActiveAsync(member.Membership, false, archived);
            await database.SetMembershipActiveAsync(member.Membership, true, restored);
            var periods = await database.GetMembershipPeriodsAsync([member.Membership.Id]);

            Assert.HasCount(2, periods);
            Assert.IsTrue(MembershipRules.IncludesDate(
                member.Membership, periods, DatabaseService.DateKey(start)));
            Assert.IsFalse(MembershipRules.IncludesDate(
                member.Membership, periods, DatabaseService.DateKey(start.AddDays(2))));
            Assert.IsTrue(MembershipRules.IncludesDate(
                member.Membership, periods, DatabaseService.DateKey(restored)));
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task NotesAreIsolatedByEventForTheSamePersonAndDate()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            await database.AddPersonToClassAsync(context.ClassGroup.Id, "Note owner");
            var events = await database.GetEventsForClassAsync(context.ClassGroup.Id);
            var attendance = events.Single(value => value.Event.SystemKey == "attendance");
            var meal = events.Single(value => value.Event.SystemKey == "meal");
            var day = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, DateTime.Today);
            var attendanceItem = (await database.GetDailyEventItemsAsync(day.Id, attendance.Event.Id)).Single();
            var mealItem = (await database.GetDailyEventItemsAsync(day.Id, meal.Event.Id)).Single();

            await database.SetEventNoteAsync(attendanceItem.Record.Id, "Attendance-only note");

            attendanceItem = (await database.GetDailyEventItemsAsync(day.Id, attendance.Event.Id)).Single();
            mealItem = (await database.GetDailyEventItemsAsync(day.Id, meal.Event.Id)).Single();
            Assert.AreEqual("Attendance-only note", attendanceItem.Note);
            Assert.AreEqual(string.Empty, mealItem.Note);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task VersionSixSharedNoteMigratesToAttendanceWithoutLeakingToMeal()
    {
        var directory = NewTemporaryDirectory();
        var path = Path.Combine(directory, "attendance.db3");
        var database = new DatabaseService(path);
        try
        {
            var context = await database.GetDefaultContextAsync();
            await database.AddPersonToClassAsync(context.ClassGroup.Id, "Legacy note owner");
            var events = await database.GetEventsForClassAsync(context.ClassGroup.Id);
            var attendance = events.Single(value => value.Event.SystemKey == "attendance");
            var meal = events.Single(value => value.Event.SystemKey == "meal");
            var day = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, DateTime.Today);
            var dayPerson = (await database.GetDailyEventItemsAsync(day.Id, attendance.Event.Id)).Single().DayPerson;
            await database.CloseAsync();

            using (var connection = new SQLite.SQLiteConnection(path))
            {
                dayPerson.Note = "Legacy shared note";
                connection.Update(dayPerson);
                connection.InsertOrReplace(new AppMetadata { Key = "SchemaVersion", Value = "6" });
            }

            database = new DatabaseService(path);
            await database.GetDefaultContextAsync();
            var attendanceItem = (await database.GetDailyEventItemsAsync(day.Id, attendance.Event.Id)).Single();
            var mealItem = (await database.GetDailyEventItemsAsync(day.Id, meal.Event.Id)).Single();
            Assert.AreEqual("Legacy shared note", attendanceItem.Note);
            Assert.AreEqual(string.Empty, mealItem.Note);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ManualBackfillRequiresExplicitRosterAndCompletesEmptyPastDay()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            context.ClassGroup.SubjectLabel = "sample point";
            await database.UpdateClassAsync(context.ClassGroup);
            var member = await database.AddPersonToClassAsync(context.ClassGroup.Id, "Backfill person");
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");
            var pastDate = DateTime.Today.AddDays(-7);

            var emptyDay = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, pastDate);
            Assert.IsEmpty(await database.GetDailyEventItemsAsync(emptyDay.Id, attendance.Event.Id));

            var candidates = await database.GetBackfillRosterCandidatesAsync(context.ClassGroup.Id, pastDate);
            Assert.HasCount(1, candidates);
            Assert.IsFalse(candidates[0].WasMemberOnDate);
            Assert.IsTrue(candidates[0].IsCurrentMember);
            var savedDay = await database.SaveBackfillRosterAsync(
                context.ClassGroup.Id,
                pastDate,
                [new BackfillRosterSelection
                {
                    MembershipId = member.Membership.Id,
                    IsExpected = true
                }],
                RosterSource.CurrentRosterAssumption);

            var repairedItems = await database.GetDailyEventItemsAsync(emptyDay.Id, attendance.Event.Id);
            Assert.HasCount(1, repairedItems);
            Assert.AreEqual("Backfill person", repairedItems[0].Name);
            Assert.IsTrue(repairedItems[0].IsExpected);
            Assert.IsTrue(savedDay.IsBackfill);
            Assert.AreEqual(RosterSource.CurrentRosterAssumption, savedDay.RosterSource);
            Assert.AreEqual(context.Organization.Name, savedDay.OrganizationNameSnapshot);
            Assert.AreEqual(context.ClassGroup.Name, savedDay.ClassNameSnapshot);
            Assert.AreEqual("sample point", savedDay.SubjectLabelSnapshot);

            await database.AddPersonToClassAsync(context.ClassGroup.Id, "Later current person");
            var unchangedItems = await database.GetDailyEventItemsAsync(emptyDay.Id, attendance.Event.Id);
            Assert.HasCount(1, unchangedItems);
            Assert.AreEqual("Backfill person", unchangedItems[0].Name);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task CompletedOnlyExportOmitsIncompleteDatesFromMatrixAndRawData()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            await database.AddPersonToClassAsync(context.ClassGroup.Id, "Export person");
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");
            var positive = attendance.StatusOptions.Single(value => value.Semantic == EventStatusSemantic.Positive);
            var completedDate = DateTime.Today;
            var incompleteDate = completedDate.AddDays(1);

            var completedDay = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, completedDate);
            var completedItem = (await database.GetDailyEventItemsAsync(completedDay.Id, attendance.Event.Id)).Single();
            await database.SetEventStatusAsync(completedItem.Record.Id, positive.Id);
            await Task.WhenAll(
                database.CompleteEventAsync(completedDay.Id, attendance.Event.Id),
                database.CompleteEventAsync(completedDay.Id, attendance.Event.Id));

            var incompleteDay = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, incompleteDate);
            var incompleteItem = (await database.GetDailyEventItemsAsync(incompleteDay.Id, attendance.Event.Id)).Single();
            await database.SetEventStatusAsync(incompleteItem.Record.Id, positive.Id);

            var exporter = new ExportService(database, directory);
            var exportPath = await exporter.ExportAsync(
                context.ClassGroup.Id,
                completedDate,
                incompleteDate,
                new ExportProfileDefinition
                {
                    EventDefinitionId = attendance.Event.Id,
                    DatesAsRows = true,
                    CompletedOnly = true,
                    IncludeRawData = true
                });

            using var archive = ZipFile.OpenRead(exportPath);
            AssertWorksheetContainsOnlyDate(archive, "xl/worksheets/sheet1.xml", completedDate, incompleteDate);
            AssertWorksheetContainsOnlyDate(archive, "xl/worksheets/sheet4.xml", completedDate, incompleteDate);
            var completions = await database.GetEventCompletionsAsync([completedDay.Id]);
            Assert.HasCount(1, completions.Where(value => value.EventDefinitionId == attendance.Event.Id));
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task CompletedHistoricalBackfillIsReportedAndExportedWithLatestDate()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            var member = await database.AddPersonToClassAsync(context.ClassGroup.Id, "Historical export person");
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");
            var positive = attendance.StatusOptions.Single(value => value.Semantic == EventStatusSemantic.Positive);
            var latestDate = DateTime.Today;
            var historicalDate = latestDate.AddDays(-1);

            context.ClassGroup.OperatingDaysMask = 1 << (int)latestDate.DayOfWeek;
            await database.UpdateClassAsync(context.ClassGroup);

            var historicalDay = await database.SaveBackfillRosterAsync(
                context.ClassGroup.Id,
                historicalDate,
                [new BackfillRosterSelection
                {
                    MembershipId = member.Membership.Id,
                    IsExpected = true
                }],
                RosterSource.CurrentRosterAssumption);
            var historicalItem = (await database.GetDailyEventItemsAsync(
                historicalDay.Id,
                attendance.Event.Id)).Single();
            await database.SetEventStatusAsync(historicalItem.Record.Id, positive.Id);
            await database.CompleteEventAsync(historicalDay.Id, attendance.Event.Id);

            var latestDay = await database.GetOrCreateClassDayAsync(context.ClassGroup.Id, latestDate);
            var latestItem = (await database.GetDailyEventItemsAsync(latestDay.Id, attendance.Event.Id)).Single();
            await database.SetEventStatusAsync(latestItem.Record.Id, positive.Id);
            await database.CompleteEventAsync(latestDay.Id, attendance.Event.Id);

            var report = await new ReportService(database).CreateEventAsync(
                context.ClassGroup.Id,
                attendance.Event.Id,
                historicalDate,
                latestDate,
                completedOnly: true);
            Assert.AreEqual(2, report.CalendarDayCount);
            Assert.AreEqual(2, report.CompletedDayCount);
            Assert.AreEqual(2, report.Overall.Positive);

            var exporter = new ExportService(database, directory);
            var exportPath = await exporter.ExportAsync(
                context.ClassGroup.Id,
                historicalDate,
                latestDate,
                new ExportProfileDefinition
                {
                    EventDefinitionId = attendance.Event.Id,
                    DatesAsRows = true,
                    CompletedOnly = true,
                    IncludeRawData = true
                });

            using var archive = ZipFile.OpenRead(exportPath);
            AssertWorksheetContainsDates(
                archive,
                "xl/worksheets/sheet1.xml",
                historicalDate,
                latestDate);
            AssertWorksheetContainsDates(
                archive,
                "xl/worksheets/sheet4.xml",
                historicalDate,
                latestDate);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task DisabledOrArchivedReminderContextIsNotSchedulable()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");
            var reminder = new EventReminder
            {
                ClassGroupId = context.ClassGroup.Id,
                EventDefinitionId = attendance.Event.Id
            };

            Assert.IsTrue(await database.IsReminderContextActiveAsync(reminder));
            await database.SetClassEventEnabledAsync(
                context.ClassGroup.Id, attendance.Event.Id, enabled: false);
            Assert.IsFalse(await database.IsReminderContextActiveAsync(reminder));
            await database.SetClassEventEnabledAsync(
                context.ClassGroup.Id, attendance.Event.Id, enabled: true);
            await database.SetClassArchivedAsync(context.ClassGroup, archived: true);
            Assert.IsFalse(await database.IsReminderContextActiveAsync(reminder));
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task FailedReminderSchedulingLeavesSavedReminderDisabled()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            var context = await database.GetDefaultContextAsync();
            var attendance = (await database.GetEventsForClassAsync(context.ClassGroup.Id))
                .Single(value => value.Event.SystemKey == "attendance");
            var reminder = new EventReminder
            {
                ClassGroupId = context.ClassGroup.Id,
                EventDefinitionId = attendance.Event.Id,
                TimeMinutes = 9 * 60,
                DaysMask = 127,
                IsEnabled = true
            };
            var coordinator = new ReminderCoordinator(database, new FailingNotificationScheduler());

            var scheduled = await coordinator.SaveAndRescheduleAsync(reminder, requestPermission: true);

            Assert.IsFalse(scheduled);
            Assert.IsFalse(reminder.IsEnabled);
            Assert.IsNotNull(coordinator.LastErrorMessage);
            StringAssert.Contains(coordinator.LastErrorMessage, "schedule failed");
            StringAssert.Contains(coordinator.LastErrorMessage, "InvalidOperationException");
            StringAssert.Contains(coordinator.LastErrorMessage, "HRESULT");
            var saved = (await database.GetEventRemindersAsync(
                context.ClassGroup.Id,
                attendance.Event.Id)).Single();
            Assert.IsFalse(saved.IsEnabled);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ConcurrentReminderRefreshesAreSerialized()
    {
        var directory = NewTemporaryDirectory();
        var database = new DatabaseService(Path.Combine(directory, "attendance.db3"));
        try
        {
            await database.GetDefaultContextAsync();
            var scheduler = new ConcurrencyTrackingNotificationScheduler();
            var coordinator = new ReminderCoordinator(database, scheduler);

            await Task.WhenAll(
                coordinator.RescheduleAsync(requestPermission: false),
                coordinator.RescheduleAsync(requestPermission: false),
                coordinator.RescheduleAsync(requestPermission: false));

            Assert.AreEqual(3, scheduler.ReplaceCalls);
            Assert.AreEqual(1, scheduler.MaximumConcurrentCalls);
        }
        finally
        {
            await TryCloseAsync(database);
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void XlsxWriterCreatesValidWorkbookPartsWithUnicode()
    {
        var requestedPath = Environment.GetEnvironmentVariable("ATTENDANCE_XLSX_SMOKE_PATH");
        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(Path.GetTempPath(), $"attendance-{Guid.NewGuid():N}.xlsx")
            : requestedPath;
        try
        {
            var matrix = new XlsxSheet { Name = "记录矩阵" };
            matrix.AddRow(["七月出勤"], style: 1);
            matrix.Merges.Add("A1:D1");
            matrix.AddRow(["日期", "宋林轩", "程冠涵", "冯恩惠"], style: 2);
            matrix.AddRow(new DateTime(2026, 7, 4), "✓", "✓", "✓");
            matrix.AddRow(new DateTime(2026, 7, 17), "✓", "✓", "✓");

            var summary = new XlsxSheet { Name = "自定义汇总" };
            summary.AddRow(["七月托管出勤"], style: 1);
            summary.Merges.Add("A1:C1");
            summary.AddRow(["姓名", "平常", "周三"], style: 2);
            summary.AddRow("宋林轩", 4, 1);
            summary.AddRow("程冠涵", 3, 0);

            var exceptions = new XlsxSheet { Name = "特殊情况" };
            exceptions.AddRow(["日期", "姓名", "状态", "备注"], style: 2);
            exceptions.AddRow(new DateTime(2026, 7, 23), "冯恩惠", "请假", "家庭安排");

            var raw = new XlsxSheet { Name = "原始数据" };
            raw.AddRow(["日期", "班级", "姓名", "出勤"], style: 2);
            raw.AddRow(new DateTime(2026, 7, 4), "托管班", "宋林轩", "出席");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SimpleXlsxWriter.Write(path, [matrix, summary, exceptions, raw]);

            using var archive = ZipFile.OpenRead(path);
            Assert.IsNotNull(archive.GetEntry("[Content_Types].xml"));
            Assert.IsNotNull(archive.GetEntry("xl/workbook.xml"));
            var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml");
            Assert.IsNotNull(worksheet);
            using var stream = worksheet.Open();
            var xml = XDocument.Load(stream);
            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.IsNotNull(xml.Root?.Element(spreadsheet + "sheetData"));
            Assert.IsTrue(xml.ToString().Contains("宋林轩", StringComparison.Ordinal));

            var styles = archive.GetEntry("xl/styles.xml");
            Assert.IsNotNull(styles);
            using var styleStream = styles.Open();
            var styleXml = XDocument.Load(styleStream);
            Assert.IsNotNull(styleXml.Root?.Element(spreadsheet + "cellXfs"));
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(requestedPath) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendance-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task TryCloseAsync(DatabaseService database)
    {
        try
        {
            await database.CloseAsync();
        }
        catch
        {
            // A test may have already closed the connection while inspecting the file.
        }
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path)
            && Path.GetFileName(path).StartsWith("attendance-tests-", StringComparison.Ordinal))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void AssertWorksheetContainsOnlyDate(
        ZipArchive archive,
        string entryName,
        DateTime included,
        DateTime excluded)
    {
        var entry = archive.GetEntry(entryName);
        Assert.IsNotNull(entry);
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var values = document.Descendants()
            .Where(element => element.Name.LocalName == "v")
            .Select(element => element.Value)
            .ToList();
        CollectionAssert.Contains(values, included.ToOADate().ToString(CultureInfo.InvariantCulture));
        CollectionAssert.DoesNotContain(values, excluded.ToOADate().ToString(CultureInfo.InvariantCulture));
    }

    private static void AssertWorksheetContainsDates(
        ZipArchive archive,
        string entryName,
        params DateTime[] dates)
    {
        var entry = archive.GetEntry(entryName);
        Assert.IsNotNull(entry);
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var values = document.Descendants()
            .Where(element => element.Name.LocalName == "v")
            .Select(element => element.Value)
            .ToList();
        foreach (var date in dates)
        {
            CollectionAssert.Contains(values, date.ToOADate().ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class FailingNotificationScheduler : ILocalNotificationScheduler
    {
        public Task<bool> EnsurePermissionAsync(bool requestPermission) => Task.FromResult(true);

        public Task ReplaceAsync(IReadOnlyList<ReminderOccurrence> occurrences) =>
            Task.FromException(new InvalidOperationException("schedule failed"));
    }

    private sealed class ConcurrencyTrackingNotificationScheduler : ILocalNotificationScheduler
    {
        private int _activeCalls;
        private int _maximumConcurrentCalls;
        private int _replaceCalls;

        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);
        public int ReplaceCalls => Volatile.Read(ref _replaceCalls);

        public Task<bool> EnsurePermissionAsync(bool requestPermission) => Task.FromResult(true);

        public async Task ReplaceAsync(IReadOnlyList<ReminderOccurrence> occurrences)
        {
            Interlocked.Increment(ref _replaceCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentCalls);
                if (active <= maximum
                    || Interlocked.CompareExchange(ref _maximumConcurrentCalls, active, maximum) == maximum)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(50);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}

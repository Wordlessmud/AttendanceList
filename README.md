# Attendance List — Android and Windows MVP 0.3

An offline-first .NET MAUI app for recording configurable, class-scoped events. It ships with Attendance and Meals, and users can add events such as Sample collection (`Collected / Not collected`), medication, permission slips, or activity completion. The UI supports English and Simplified Chinese.

## Release status

Version `0.3.0` is the first public source release. A publicly trusted signed Windows MSIX is not yet available.

This has an important Windows limitation: the reminder/notification feature depends on an installed MSIX package with trusted package identity. An unpackaged executable will not support the Windows reminder scheduler, an unsigned MSIX cannot be installed normally, and a self-signed test MSIX requires each user to manually trust the developer certificate. Until a trusted certificate or Microsoft Store/SignPath signing is available, Windows reminders are therefore suitable only for development and controlled testing, not general public distribution.

See [`docs/WINDOWS_MSIX.md`](docs/WINDOWS_MSIX.md) for packaging and signing details and [`docs/RELEASING.md`](docs/RELEASING.md) for the release process.

## Implemented features

- Multiple organizations and independently managed classes.
- Per-class membership, archive state, ordering, expected weekdays, operating days, and calendar overrides.
- Organization event definitions with editable names and two or more status options.
- Status semantics: positive, negative, neutral, and excused; system `Unmarked` is always separate from a real business status.
- Events can be enabled or disabled independently for each class.
- Shared person/date notes for special circumstances.
- Independent completion and explicit reopening for every class event.
- Local event reminders by time and weekday, optionally limited to class operating days and incomplete events.
- First-install onboarding for workspace naming, event concepts, notification permission, history, and export.
- History distinguishes an expected date with no record from an existing but incomplete event.
- Class/event/date-range reports and configurable `.xlsx` export profiles:
  - dates or people as rows;
  - user-selected positive symbols;
  - a base/regular count column;
  - custom summary buckets matched by weekday, exact date, or calendar label;
  - optional exclusion of custom buckets from the base count;
  - event matrix, custom summary, exceptions/notes, and optional raw-data sheets.
- Local SQLite schema-v3 storage with migration from attendance-only and schema-v2 databases.

## Daily workflow

1. Create or select an organization and class under **Classes and people**.
2. Configure members, operating days, calendar exceptions, and enabled events.
3. Under **Events**, rename built-ins or add a custom event and status labels.
4. Optionally add one or more reminders for each class event.
5. Open **Today**, choose a class and event, record statuses, and add notes.
6. Review unmarked people and complete that event independently.
7. Use **History** to inspect or backfill dates.
8. Use **Reports** to audit completeness and export Excel.

## Missing dates and statistics

Reports enumerate the class calendar, not only rows already stored in SQLite:

- excluded dates are not regular operating days;
- included/special dates are operating days;
- an operating date without a `ClassDay` is **Missing**;
- a class day without completion for the selected event is **Incomplete**;
- neither state is silently converted to a negative status or zero;
- completed-only mode excludes unfinished records from official totals.

Each event status has a semantic category. The report's positive rate is positive statuses divided by all counted statuses. Events configured to use the expected roster exclude not-expected people from aggregation; other events include the class-day roster.

## Reminders

Reminder schedules are persisted in SQLite and refreshed on app launch, after edits, and after event completion. Android uses inexact idle-compatible alarms and requests notification permission on Android 13+. Windows uses scheduled app notifications. The operating system may deliver reminders a few minutes late, and a device that is powered off can miss a scheduled occurrence; the app regenerates the next 45 days whenever it opens.

Reminder content defaults to class and event names and does not include person names.

Windows reminder scheduling requires package identity, so Windows releases must be installed as MSIX packages or through Microsoft Store. Running an executable directly from the publish folder is unsupported. Without a trusted signing certificate, a general-public Windows build cannot currently provide a normal install-and-use reminder experience. Self-signed packages are for controlled testing only because users must manually trust the certificate. If scheduling fails, the reminder is retained but automatically disabled instead of appearing active, and diagnostic details are written to `notification-errors.log` in the app data directory. See `docs/WINDOWS_MSIX.md`.

## Data storage and migration

The primary database is `attendance.db3` in the platform app-data directory. Before schema-v7 initialization, an existing database is copied to a timestamped `attendance.pre-v7.*.db3` backup.

Schema v3 adds:

- `EventDefinitions` and `EventStatusOptions`;
- `ClassEvents`;
- `EventRecords` and `EventCompletions`;
- `EventReminders`.

Existing `TrackingRecords` and `TrackingCompletions` are migrated to the built-in Attendance and Meals events. Legacy tables remain as a recovery source. Working data remains local unless the user explicitly shares an export. Removing the app may remove local data; full user-facing backup/restore remains production work.

Later migrations add membership periods, immutable historical roster and naming snapshots, and event-specific notes. The current schema version is v7.

## Build and test

Use Windows with the .NET MAUI workload and .NET 10 SDK:

```powershell
dotnet restore
dotnet workload restore
dotnet build AttendanceList/AttendanceList.csproj -f net10.0-windows10.0.19041.0
dotnet build AttendanceList/AttendanceList.csproj -f net10.0-android
dotnet test AttendanceList.Tests/AttendanceList.Tests.csproj
```

Tests cover migrations, event-specific notes, historical backfill/export, generic event semantics, reminder failure handling, weekday masks, and generated XLSX package structure.

## Project documents

- [`CHANGELOG.md`](CHANGELOG.md)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)
- [`SECURITY.md`](SECURITY.md)
- [`docs/RELEASING.md`](docs/RELEASING.md)
- [`docs/WINDOWS_MSIX.md`](docs/WINDOWS_MSIX.md)

## Remaining production work

1. Full database backup/restore and roster import.
2. Optional mapping into externally supplied Excel templates.
3. Reminder deep links.
4. Drag-and-drop ordering and bulk roster editing.
5. Accessibility, keyboard navigation, and device UI automation.
6. Signed Windows and Android release packages.

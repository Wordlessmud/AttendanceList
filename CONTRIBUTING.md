# Contributing to Attendance List

Thank you for helping improve Attendance List.

## Before opening a change

- Search existing issues and pull requests to avoid duplicates.
- Keep changes focused. Separate unrelated fixes into separate pull requests.
- Do not include real attendance records, personal information, database files, signing keys or certificates.
- For significant behaviour or schema changes, open an issue first so the design and migration path can be discussed.

## Development setup

Use Windows with the .NET 10 SDK and the .NET MAUI workload installed.

```powershell
git clone https://github.com/Wordlessmud/AttendanceList.git
cd AttendanceList

dotnet restore
dotnet workload restore
dotnet test .\AttendanceList.Tests\AttendanceList.Tests.csproj
```

Build the platform targets:

```powershell
dotnet build .\AttendanceList\AttendanceList.csproj `
  -f net10.0-windows10.0.19041.0

dotnet build .\AttendanceList\AttendanceList.csproj `
  -f net10.0-android
```

## Pull requests

A pull request should:

- explain the problem and the proposed solution;
- include tests for behaviour changes where practical;
- preserve offline operation and user-data privacy;
- include a database migration when persisted models change;
- update English and Simplified Chinese resources for user-visible text;
- update documentation when installation, release or user workflows change;
- pass `dotnet test` before review.

## Reminder and notification changes

Windows reminder scheduling depends on MSIX package identity. Test Windows notification changes using an installed MSIX launched from the Start menu, not a loose executable from the publish directory.

Android notification changes should be tested on a supported Android version with notification permission both granted and denied.

## Database changes

Never edit an existing released migration in a way that changes its historical meaning. Add a new schema version and test migration from at least:

- a fresh database;
- the immediately preceding schema;
- any older schema materially affected by the change.

## Licence

By contributing, you agree that your contribution is licensed under the GNU General Public License version 3, the same licence as the project.

# Release process

This document describes the first public release and later releases.

## Release channels

Attendance List can be distributed through:

1. **GitHub source release** — always available and does not require code signing.
2. **Microsoft Store** — Microsoft signs the submitted package.
3. **Signed GitHub binary release** — use SignPath Foundation or another publicly trusted signing service.
4. **Developer/test MSIX** — self-signed and suitable only for devices where the test certificate is installed manually.

Do not publish an unsigned MSIX to end users. Windows requires a deployable MSIX to be signed, and the signing identity must be trusted on the user's device.

## Versioning

Keep these values aligned:

- `AttendanceList/AttendanceList.csproj`
  - `ApplicationDisplayVersion`, for example `0.3.0`;
  - `ApplicationVersion`, an increasing integer;
  - `PackageVersion`.
- `AttendanceList/Platforms/Windows/Package.appxmanifest`
  - four-part `Identity Version`, for example `0.3.0.0`.
- `CHANGELOG.md`.
- Git tag, for example `v0.3.0`.

Never reuse a published package version.

## Pre-release checklist

Run from the repository root on Windows:

```powershell
dotnet restore
dotnet workload restore
dotnet test .\AttendanceList.Tests\AttendanceList.Tests.csproj

dotnet build .\AttendanceList\AttendanceList.csproj `
  -f net10.0-windows10.0.19041.0 `
  -c Release

dotnet build .\AttendanceList\AttendanceList.csproj `
  -f net10.0-android `
  -c Release
```

Then verify:

- a fresh installation can complete onboarding;
- an existing schema-v6 or schema-v7 test database opens successfully;
- an event can be recorded, completed, reopened and exported;
- the generated XLSX opens in Excel or LibreOffice;
- Windows reminders schedule in an installed MSIX launched from Start;
- denied notification permission produces a useful error instead of a crash;
- no real data, signing secrets, build output or database files are committed;
- `CHANGELOG.md` contains the final release date and notes.

## First source release: v0.3.0

The initial public release should be clearly labelled as a source-first preview because a publicly trusted signed Windows installer is not yet attached.

Suggested release title:

```text
Attendance List v0.3.0 — first public source release
```

Suggested release notes are stored in `docs/release-notes/v0.3.0.md`.

Create a GitHub release from the `master` commit after the documentation pull request is merged:

1. Open **Releases** in GitHub.
2. Select **Draft a new release**.
3. Create tag `v0.3.0` targeting `master`.
4. Use the title above.
5. Paste `docs/release-notes/v0.3.0.md` into the release description.
6. Mark it as a pre-release while signed binaries are unavailable.
7. Publish the release.

GitHub automatically provides source-code ZIP and tarball downloads for the tagged commit.

## Building a test MSIX

Create a self-signed certificate whose subject exactly matches the current manifest publisher (`CN=AttendanceList`) and run:

```powershell
.\scripts\publish-msix.ps1 `
  -RuntimeIdentifier win-x64 `
  -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

This package is for development and controlled testing only. Do not ask general users to trust a self-signed root certificate.

## Building for external signing

```powershell
.\scripts\publish-msix.ps1 `
  -RuntimeIdentifier win-x64 `
  -Unsigned
```

Submit the resulting artifact to the approved signing service. After signing, verify the signature and install the exact signed artifact on a clean Windows test account before attaching it to a release.

## SignPath Foundation route

SignPath Foundation offers free code signing to qualifying open-source projects. The project must already be released in the form that will be signed, so publish the source-first `v0.3.0` release before applying.

Before applying:

- keep the repository public;
- retain the GPL-3.0 licence;
- ensure every bundled component is open source or an allowed system library;
- keep the project actively maintained;
- make the build reproducible through a documented CI workflow;
- publish the unsigned artifact only to the signing workflow, not to end users.

After acceptance, update the MSIX manifest `Publisher` to the exact subject assigned by SignPath and configure the approved build/signing workflow. Do not guess or abbreviate the publisher string.

## Microsoft Store route

Reserve the application name in Partner Center and associate the project with the Store. Store association replaces the placeholder package identity. Use the exact identity supplied by Partner Center and do not restore `CN=AttendanceList` afterward.

## Release integrity

For each binary release, record:

- tag and commit SHA;
- package filename;
- package version and architecture;
- signing provider;
- SHA-256 checksum;
- test result summary.

Do not replace an already published binary under the same version. Publish a new version instead.

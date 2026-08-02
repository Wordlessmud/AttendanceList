# Changelog

All notable changes to Attendance List are documented here.

The project follows [Semantic Versioning](https://semver.org/). During the `0.x` series, minor versions may contain breaking changes while the application and data model are still stabilising.

## [Unreleased]

### Planned

- Signed Windows MSIX and Android release packages.
- Full database backup and restore.
- Roster import and bulk editing.
- Accessibility and keyboard-navigation improvements.

## [0.3.0] — 2026-08-02

First public source release.

### Added

- Offline-first .NET MAUI application for Windows and Android.
- Multiple organisations, classes, memberships and operating calendars.
- Configurable event definitions and status semantics.
- Attendance, meals and custom event workflows.
- Local reminders by time and weekday.
- History, reporting and configurable XLSX export.
- English and Simplified Chinese user interface.
- SQLite schema migration through version 7.

### Fixed

- Windows reminder scheduling now requires genuine MSIX package identity.
- Reminder rescheduling operations are serialised to prevent overlapping schedule replacement.
- Notification failures include exception type and HRESULT and are logged to `notification-errors.log`.

### Distribution note

The source release is public, but a generally installable Windows binary will follow after the MSIX package has been signed through Microsoft Store, SignPath Foundation or another trusted signing service.

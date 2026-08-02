# MVP 0.3 behaviour specification

## Scope hierarchy

- An organization owns classes and reusable event definitions.
- A class independently enables an organization's events.
- People join classes through memberships; expected weekdays belong to the membership.
- Every record is scoped to one class, date, person, and event.

## Configurable events

An event definition has a user-editable name, a positive export symbol, an expected-roster rule, and two or more editable status options. Each option has a label, symbol, order, and semantic category: Positive, Negative, Neutral, or Excused.

`Unmarked` is represented by `StatusOptionId = 0`; it is not a configurable status option. Consequently, a custom event's real `Not collected` state is never conflated with missing input.

Attendance and Meals are built-in event definitions. Schema-v2 status values migrate by option order:

- Attendance: Present, Absent, Excused.
- Meals: Ate, Did not eat.

Each class/event/date has an independent completion row. Completed events are read-only until reopened.

## Notes and calendar

A class-day person has one shared note, visible across that day's events. Expected state is snapshotted when the class-day person is created. Calendar overrides can exclude a date, add an operating date, or add a special label.

For reports and history:

- an operating date without a class day is **Missing**;
- a class day without selected-event completion is **Incomplete**;
- neither becomes a negative status or zero automatically;
- missing dates can be created and backfilled.

## Reminders

A reminder belongs to one class and one event. It stores local time, weekday mask, start/end dates, enabled state, optional message, operating-day filtering, and incomplete-only filtering.

The app schedules a rolling 45-day window. It refreshes schedules on startup, reminder edits, enable/disable changes, and event completion/reopening. Default notification content contains only class/event names. Delivery is best-effort and controlled by the operating system.

## Reporting and export

Event reports aggregate status semantics and keep Unmarked separate. If the event uses the expected roster, not-expected people are excluded from aggregate totals.

An export profile stores an `EventDefinitionId`, matrix orientation, completed-only behavior, positive symbol, notes/raw-data options, base-column name, and custom date buckets. Legacy profiles with only `TrackingKind` are mapped to built-in events when loaded.

Exports contain:

1. date/person event matrix;
2. base and custom positive-count summary;
3. exceptions and person-day notes;
4. optional raw event records.

Custom buckets match weekdays, exact dates, or calendar labels and may be excluded from the base count.

## Onboarding and localization

New installations see a five-step onboarding guide. It explains local storage, allows starter organization/class renaming, explains events and Unmarked, requests notification permission only after user action, and introduces history/export. Existing upgraded installations are not interrupted; Settings can reopen the guide.

English is neutral; Simplified Chinese is included. Culture, onboarding completion, button size, and current organization/class are local preferences.

## Storage and migration

Schema version 3 adds event definition/status, class enablement, generic record/completion, and reminder tables. Before migration an existing database receives a timestamped `attendance.pre-v3.*.db3` copy. Schema-v1 attendance data first migrates through the schema-v2 class model, then to generic events. Legacy tables are retained.

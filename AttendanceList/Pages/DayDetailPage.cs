using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class DayDetailPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly MotionService _motion;
    private readonly ClassDay _day;
    private readonly VerticalStackLayout _content = new() { Spacing = 10 };

    public DayDetailPage(DatabaseService database, MotionService motion, ClassDay day)
    {
        _database = database;
        _motion = motion;
        _day = day;
        Title = LocalizationService.FormatDateKey(day.DateKey);
        Padding = new Thickness(16);
        Content = new ScrollView { Content = _content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadAsync);
    }

    private async Task LoadAsync()
    {
        var events = await _database.GetEventsForClassAsync(_day.ClassGroupId);
        _content.Clear();
        if (_day.IsBackfill)
        {
            var sourceKey = _day.RosterSource switch
            {
                RosterSource.HistoricalMembership => "RosterSourceHistorical",
                RosterSource.CurrentRosterAssumption => "RosterSourceCurrentAssumption",
                _ => "RosterSourceManual"
            };
            var sourceText = string.Format(
                LocalizationService.T("BackfillRosterSource"),
                LocalizationService.T(sourceKey));
            _content.Add(Ui.Card(Ui.Secondary(sourceText)));
        }
        foreach (var definition in events)
        {
            var items = await _database.GetDailyEventItemsAsync(_day.Id, definition.Event.Id);
            var relevant = (definition.Event.UseExpectedRoster
                    ? items.Where(i => i.IsExpected)
                    : items)
                .ToList();
            var summary = ReportService.Summarise(relevant, definition.StatusOptions);
            var completion = await _database.GetEventCompletionAsync(_day.Id, definition.Event.Id);
            var edit = Ui.SecondaryButton(LocalizationService.T("Edit"));
            edit.Clicked += async (_, _) => await Navigation.PushAsync(new ReviewPage(
                _database,
                _motion,
                _day.Id,
                definition.Event,
                definition.StatusOptions,
                SubjectLabels.ForSnapshot(_day.SubjectLabelSnapshot),
                unresolvedOnly: false));
            var completionAction = Ui.SecondaryButton(completion is null
                ? LocalizationService.T("MarkComplete")
                : LocalizationService.T("Reopen"));
            SemanticProperties.SetDescription(
                completionAction,
                $"{completionAction.Text}: {definition.Name}");
            completionAction.Clicked += async (_, _) => await Ui.RunSafelyAsync(
                this,
                () => ToggleCompletionAsync(
                    definition,
                    relevant,
                    isCompleted: completion is not null,
                    completionAction));
            var header = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            header.Add(new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    Ui.Heading(definition.Name, 18),
                    Ui.Secondary(EventSummaryFormatter.Format(summary, definition.StatusOptions)),
                    Ui.Secondary(completion is null ? LocalizationService.T("Open") : LocalizationService.T("Completed"))
                }
            }, 0, 0);
            header.Add(new VerticalStackLayout
            {
                Spacing = 6,
                Children = { edit, completionAction }
            }, 1, 0);
            _content.Add(Ui.Card(header));

            var optionById = definition.StatusOptions.ToDictionary(o => o.Id);
            foreach (var item in items)
            {
                optionById.TryGetValue(item.StatusOptionId, out var status);
                var text = status is null
                    ? LocalizationService.T("Unknown")
                    : $"{status.Symbol} {status.Label}";
                var note = string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" · 📝 {item.Note}";
                var personRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    }
                };
                personRow.Add(new Label
                {
                    Text = item.Name + note,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);
                personRow.Add(new Label { Text = text, FontAttributes = FontAttributes.Bold }, 1, 0);
                _content.Add(Ui.Card(personRow, new Thickness(12, 7)));
            }
        }
        if (events.Count == 0)
        {
            _content.Add(Ui.Secondary(LocalizationService.T("NoEventsEnabled")));
        }
    }

    private async Task ToggleCompletionAsync(
        ClassEventDefinition definition,
        IReadOnlyList<DailyEventItem> relevant,
        bool isCompleted,
        Button action)
    {
        action.IsEnabled = false;
        try
        {
            if (isCompleted)
            {
                await _database.ReopenEventAsync(_day.Id, definition.Event.Id);
            }
            else
            {
                var unresolved = relevant.Count(item => item.StatusOptionId == 0);
                if (unresolved > 0)
                {
                    var confirmed = await DisplayAlertAsync(
                        LocalizationService.T("UnresolvedRecords"),
                        string.Format(LocalizationService.T("UnresolvedRecordsMessage"), unresolved),
                        LocalizationService.T("FinishAnyway"),
                        LocalizationService.T("Cancel"));
                    if (!confirmed)
                    {
                        return;
                    }
                }
                await _database.CompleteEventAsync(_day.Id, definition.Event.Id);
            }
            await LoadAsync();
        }
        finally
        {
            action.IsEnabled = true;
        }
    }
}

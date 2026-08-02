using System.Collections.ObjectModel;
using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class HistoryPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassContextService _context;
    private readonly MotionService _motion;
    private readonly ObservableCollection<ClassDayHistoryRow> _rows = [];
    private readonly CollectionView _collection;
    private readonly Label _classLabel = Ui.Secondary();
    private readonly DatePicker _datePicker = new() { Date = DateTime.Today };

    public HistoryPage(
        DatabaseService database,
        ReportService reports,
        ClassContextService context,
        MotionService motion)
    {
        _database = database;
        _context = context;
        _motion = motion;
        Title = LocalizationService.T("History");
        Padding = new Thickness(16);
        _datePicker.Format = LocalizationService.DatePickerFormat;
        var open = new Button { Text = LocalizationService.T("OpenDate") };
        open.Clicked += OpenDateClicked;
        var dateRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        dateRow.Add(_datePicker, 0, 0);
        dateRow.Add(open, 1, 0);
        _collection = new CollectionView
        {
            ItemsSource = _rows,
            SelectionMode = SelectionMode.Single,
            EmptyView = Ui.Secondary(LocalizationService.T("NoHistory")),
            ItemTemplate = new DataTemplate(() =>
            {
                var date = Ui.Heading(string.Empty, 18);
                date.SetBinding(Label.TextProperty, nameof(ClassDayHistoryRow.DateText));
                var summary = Ui.Secondary();
                summary.SetBinding(Label.TextProperty, nameof(ClassDayHistoryRow.SummaryText));
                var state = Ui.Secondary();
                state.SetBinding(Label.TextProperty, nameof(ClassDayHistoryRow.StateText));
                return Ui.Card(new VerticalStackLayout { Spacing = 4, Children = { date, summary, state } }, new Thickness(14));
            })
        };
        _collection.SelectionChanged += SelectionChanged;
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 10
        };
        root.Add(Ui.Card(_classLabel, new Thickness(12)), 0, 0);
        root.Add(Ui.Card(dateRow, new Thickness(8)), 0, 1);
        root.Add(_collection, 0, 2);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadAsync);
    }

    private async Task LoadAsync()
    {
        await _context.EnsureInitializedAsync();
        if (_context.CurrentClass is null)
        {
            return;
        }
        var classGroup = _context.CurrentClass;
        _classLabel.Text = $"{LocalizationService.T("Class")}: {classGroup.Name}";
        var end = DateTime.Today;
        var start = end.AddDays(-89);
        var events = await _database.GetEventsForClassAsync(classGroup.Id);
        var days = await _database.GetClassDaysAsync(classGroup.Id, start, end);
        var overrides = await _database.GetDayOverridesAsync(classGroup.Id, start, end);
        var dayIds = days.Select(day => day.Id).ToList();
        var people = await _database.GetClassDayPeopleAsync(dayIds);
        var records = await _database.GetEventRecordsAsync(people.Select(person => person.Id));
        var completions = await _database.GetEventCompletionsAsync(dayIds);
        var daysByDate = days.ToDictionary(d => d.DateKey);
        var overridesByDate = overrides.ToDictionary(value => value.DateKey);
        var peopleByDay = people
            .GroupBy(person => person.ClassDayId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var recordByPersonAndEvent = records
            .GroupBy(record => (record.ClassDayPersonId, record.EventDefinitionId))
            .ToDictionary(group => group.Key, group => group.First());
        var completionKeys = completions
            .Select(completion => (completion.ClassDayId, completion.EventDefinitionId))
            .ToHashSet();
        _rows.Clear();
        for (var date = end; date >= start; date = date.AddDays(-1))
        {
            var dateKey = DatabaseService.DateKey(date);
            overridesByDate.TryGetValue(dateKey, out var dayOverride);
            var operating = dayOverride?.Kind switch
            {
                DayOverrideKind.Excluded => false,
                DayOverrideKind.Included or DayOverrideKind.Special => true,
                _ => DatabaseService.HasDay(classGroup.OperatingDaysMask, date.DayOfWeek)
            };
            daysByDate.TryGetValue(dateKey, out var day);
            if (!operating && day is null)
            {
                continue;
            }
            if (day is null)
            {
                _rows.Add(new ClassDayHistoryRow
                {
                    Date = date,
                    IsMissing = true,
                    DateText = LocalizationService.FormatDate(date, "D"),
                    SummaryText = LocalizationService.T("NoRecordForExpectedDay"),
                    StateText = LocalizationService.T("Missing")
                });
                continue;
            }
            var summaries = new List<string>();
            var states = new List<string>();
            var dayPeople = peopleByDay.GetValueOrDefault(day.Id) ?? [];
            foreach (var definition in events)
            {
                var relevant = definition.Event.UseExpectedRoster
                    ? dayPeople.Where(person => person.IsExpected)
                    : dayPeople;
                var optionById = definition.StatusOptions.ToDictionary(option => option.Id);
                var semantics = relevant.Select(person =>
                {
                    if (!recordByPersonAndEvent.TryGetValue((person.Id, definition.Event.Id), out var record)
                        || !optionById.TryGetValue(record.StatusOptionId, out var option))
                    {
                        return (EventStatusSemantic?)null;
                    }
                    return option.Semantic;
                });
                var summary = EventRules.Summarise(semantics);
                summaries.Add($"{definition.Name}: {EventSummaryFormatter.Format(summary, definition.StatusOptions)}");
                var completed = completionKeys.Contains((day.Id, definition.Event.Id));
                states.Add($"{definition.Name}: {(completed ? LocalizationService.T("Completed") : LocalizationService.T("Open"))}");
            }
            _rows.Add(new ClassDayHistoryRow
            {
                Day = day,
                Date = date,
                DateText = LocalizationService.FormatDate(date, "D"),
                SummaryText = string.Join(" · ", summaries),
                StateText = string.Join(" · ", states)
            });
        }
    }

    private async void OpenDateClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        await OpenOrBackfillAsync(_datePicker.Date ?? DateTime.Today);
    }

    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ClassDayHistoryRow row || _context.CurrentClass is null)
        {
            return;
        }
        _collection.SelectedItem = null;
        await OpenOrBackfillAsync(row.Date, row.Day);
    }

    private async Task OpenOrBackfillAsync(DateTime date, ClassDay? knownDay = null)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var day = knownDay ?? await _database.GetClassDayAsync(_context.CurrentClass.Id, date);
        if (day is not null)
        {
            var people = await _database.GetClassDayPeopleAsync([day.Id]);
            if (people.Count > 0)
            {
                await Navigation.PushAsync(new DayDetailPage(_database, _motion, day));
                return;
            }
        }
        await Navigation.PushAsync(new BackfillRosterPage(_database, _motion, _context.CurrentClass, date));
    }
}

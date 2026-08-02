using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class TodayPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassContextService _context;
    private readonly ReminderCoordinator _reminders;
    private readonly MotionService _motion;
    private readonly Picker _classPicker = new();
    private readonly Picker _eventPicker = new();
    private readonly Label _dateLabel = Ui.Heading(string.Empty, 22);
    private readonly Label _summaryLabel = Ui.Secondary();
    private readonly Label _stateLabel = Ui.Secondary();
    private readonly VerticalStackLayout _people = new() { Spacing = 8 };
    private readonly Button _reviewButton = new();
    private readonly Button _finishButton = new();
    private List<ClassGroup> _classes = [];
    private List<ClassEventDefinition> _events = [];
    private List<DailyEventItem> _items = [];
    private ClassDay? _day;
    private ClassEventDefinition? _selectedEvent;
    private bool _completed;
    private bool _loading;

    public TodayPage(
        DatabaseService database,
        ClassContextService context,
        ReminderCoordinator reminders,
        MotionService motion)
    {
        _database = database;
        _context = context;
        _reminders = reminders;
        _motion = motion;
        Title = LocalizationService.T("Today");
        Padding = new Thickness(16);

        _classPicker.Title = LocalizationService.T("Class");
        _classPicker.ItemDisplayBinding = new Binding(nameof(ClassGroup.Name));
        _classPicker.SelectedIndexChanged += ClassChanged;
        _eventPicker.Title = LocalizationService.T("RecordType");
        _eventPicker.ItemDisplayBinding = new Binding(nameof(ClassEventDefinition.Name));
        _eventPicker.SelectedIndexChanged += EventChanged;
        _reviewButton.Text = LocalizationService.T("ReviewRemaining");
        _reviewButton.Clicked += ReviewClicked;
        _finishButton.Text = LocalizationService.T("FinishDay");
        _finishButton.Clicked += FinishClicked;

        var selectors = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        selectors.Add(_classPicker, 0, 0);
        selectors.Add(_eventPicker, 1, 0);
        var header = Ui.Card(new VerticalStackLayout
        {
            Spacing = 5,
            Children = { selectors, _dateLabel, _summaryLabel, _stateLabel }
        });
        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        actions.Add(_reviewButton, 0, 0);
        actions.Add(_finishButton, 1, 0);
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 12
        };
        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = _people }, 0, 1);
        root.Add(actions, 0, 2);
        root.Add(Ui.Watermark(LocalizationService.T("WatermarkText")), 0, 3);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadSelectorsAsync);
    }

    private async Task LoadSelectorsAsync()
    {
        await _context.EnsureInitializedAsync();
        if (_context.CurrentOrganization is null || _context.CurrentClass is null)
        {
            return;
        }
        _loading = true;
        _classes = (await _database.GetClassesAsync(_context.CurrentOrganization.Id)).ToList();
        _classPicker.ItemsSource = _classes;
        _classPicker.SelectedIndex = _classes.FindIndex(c => c.Id == _context.CurrentClass.Id);
        await LoadEventsAsync();
        _loading = false;
        await LoadDayAsync();
    }

    private async Task LoadEventsAsync()
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var previousId = _selectedEvent?.Event.Id;
        _events = (await _database.GetEventsForClassAsync(_context.CurrentClass.Id)).ToList();
        _eventPicker.ItemsSource = _events;
        var index = _events.FindIndex(e => e.Event.Id == previousId);
        _eventPicker.SelectedIndex = index >= 0 ? index : (_events.Count > 0 ? 0 : -1);
        _selectedEvent = _eventPicker.SelectedItem as ClassEventDefinition;
    }

    private async Task LoadDayAsync()
    {
        if (_context.CurrentClass is null || _selectedEvent is null)
        {
            _people.Clear();
            _summaryLabel.Text = LocalizationService.T("NoEventsEnabled");
            return;
        }
        _day = await _database.GetClassDayAsync(_context.CurrentClass.Id, DateTime.Today);
        if (_day is null)
        {
            _items = (await _database.GetDailyEventPreviewAsync(
                _context.CurrentClass.Id,
                DateTime.Today,
                _selectedEvent.Event.Id,
                activeMembersOnly: true)).ToList();
            _completed = false;
        }
        else
        {
            _items = (await _database.GetDailyEventItemsAsync(
                _day.Id,
                _selectedEvent.Event.Id,
                activeMembersOnly: true)).ToList();
            _completed = await _database.GetEventCompletionAsync(_day.Id, _selectedEvent.Event.Id) is not null;
        }
        _dateLabel.Text = LocalizationService.FormatDate(DateTime.Today, "D");
        Render();
    }

    private void Render(bool renderPeople = true)
    {
        if (_selectedEvent is null)
        {
            return;
        }
        var options = _selectedEvent.StatusOptions;
        var optionById = options.ToDictionary(o => o.Id);
        var visible = _selectedEvent.Event.UseExpectedRoster
            ? _items.Where(i => i.IsExpected).ToList()
            : _items;
        var summary = ReportService.Summarise(visible, options);
        _summaryLabel.Text = EventSummaryFormatter.Format(summary, options);
        _stateLabel.Text = _completed
            ? LocalizationService.T("Completed")
            : LocalizationService.T("Open");
        _finishButton.Text = _completed
            ? LocalizationService.T("Reopen")
            : LocalizationService.T("FinishDay");
        _reviewButton.IsEnabled = !_completed && visible.Any(i => i.StatusOptionId == 0);

        if (!renderPeople)
        {
            return;
        }
        _people.Clear();
        if (_items.Count == 0)
        {
            _people.Add(Ui.Secondary(string.Format(
                LocalizationService.T("NoSubjectsHint"),
                SubjectLabels.ForClass(_context.CurrentClass))));
            return;
        }
        var positive = options.FirstOrDefault(o => o.Semantic == EventStatusSemantic.Positive) ?? options.FirstOrDefault();
        foreach (var item in _items)
        {
            optionById.TryGetValue(item.StatusOptionId, out var selected);
            var button = new Button
            {
                Text = selected is null ? LocalizationService.T("Unknown") : $"{selected.Symbol} {selected.Label}",
                IsEnabled = !_completed,
                BackgroundColor = StatusColor(selected?.Semantic),
                TextColor = Color.FromArgb("#0F172A"),
                HorizontalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetDescription(button, $"{item.Name}: {button.Text}");
            button.Clicked += async (_, _) =>
            {
                if (positive is null)
                {
                    return;
                }
                var nextStatus = item.StatusOptionId == positive.Id ? 0 : positive.Id;
                var personId = item.DayPerson.PersonId;
                button.IsEnabled = false;
                try
                {
                    var persisted = await EnsureDayForWriteAsync(personId);
                    if (persisted is null)
                    {
                        return;
                    }
                    await _database.SetEventStatusAsync(persisted.Record.Id, nextStatus);
                    persisted.Record.StatusOptionId = nextStatus;
                    Render();
                }
                finally
                {
                    button.IsEnabled = !_completed;
                }
            };
            var note = Ui.SecondaryButton(
                string.IsNullOrWhiteSpace(item.Note) ? "+ 📝" : "📝",
                $"{string.Format(LocalizationService.T("SubjectNote"), SubjectLabels.ForClass(_context.CurrentClass))}: {item.Name}");
            note.Clicked += async (_, _) => await EditNoteAsync(item, note);
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(150)),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };
            row.Add(new Label
            {
                Text = item.Name + (item.IsExpected ? string.Empty : " ·"),
                FontAttributes = FontAttributes.Bold,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, 0);
            row.Add(button, 1, 0);
            row.Add(note, 2, 0);
            _people.Add(Ui.Card(row, new Thickness(10, 6)));
        }
    }

    private async Task EditNoteAsync(DailyEventItem item, Button button)
    {
        var note = await DisplayPromptAsync(
            string.Format(
                LocalizationService.T("SubjectNote"),
                SubjectLabels.ForClass(_context.CurrentClass)),
            item.Name,
            initialValue: item.Note,
            maxLength: 2000,
            keyboard: Keyboard.Text);
        if (note is null)
        {
            return;
        }
        var persisted = await EnsureDayForWriteAsync(item.DayPerson.PersonId);
        if (persisted is null)
        {
            return;
        }
        item = persisted;
        await _database.SetEventNoteAsync(item.Record.Id, note);
        item.Record.Note = note.Trim();
        button.Text = string.IsNullOrWhiteSpace(item.Note) ? "+ 📝" : "📝";
    }

    private async void ClassChanged(object? sender, EventArgs e)
    {
        if (_loading || _classPicker.SelectedItem is not ClassGroup classGroup)
        {
            return;
        }
        _context.SetClass(classGroup);
        _loading = true;
        await LoadEventsAsync();
        _loading = false;
        await LoadDayAsync();
    }

    private async void EventChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }
        _selectedEvent = _eventPicker.SelectedItem as ClassEventDefinition;
        await LoadDayAsync();
    }

    private async void ReviewClicked(object? sender, EventArgs e)
    {
        if (_selectedEvent is not null)
        {
            await EnsureDayForWriteAsync();
        }
        if (_day is not null && _selectedEvent is not null)
        {
            await Navigation.PushAsync(new ReviewPage(
                _database,
                _motion,
                _day.Id,
                _selectedEvent.Event,
                _selectedEvent.StatusOptions,
                SubjectLabels.ForClass(_context.CurrentClass),
                unresolvedOnly: true,
                activeMembersOnly: true));
        }
    }

    private async void FinishClicked(object? sender, EventArgs e)
    {
        if (_selectedEvent is null)
        {
            return;
        }
        await EnsureDayForWriteAsync();
        if (_day is null)
        {
            return;
        }
        if (_completed)
        {
            await _database.ReopenEventAsync(_day.Id, _selectedEvent.Event.Id);
        }
        else
        {
            var relevant = _selectedEvent.Event.UseExpectedRoster ? _items.Where(i => i.IsExpected) : _items;
            var unknown = relevant.Count(i => i.StatusOptionId == 0);
            if (unknown > 0)
            {
                var finish = await DisplayAlertAsync(
                    LocalizationService.T("UnresolvedRecords"),
                    string.Format(LocalizationService.T("UnresolvedRecordsMessage"), unknown),
                    LocalizationService.T("FinishAnyway"),
                    LocalizationService.T("Cancel"));
                if (!finish)
                {
                    return;
                }
            }
            await _database.CompleteEventAsync(_day.Id, _selectedEvent.Event.Id);
        }
        await _reminders.RescheduleAsync(requestPermission: false);
        await LoadDayAsync();
        await _motion.EmphasizeStateAsync(_stateLabel);
    }

    private async Task<DailyEventItem?> EnsureDayForWriteAsync(int? personId = null)
    {
        if (_context.CurrentClass is null || _selectedEvent is null)
        {
            return null;
        }
        if (_day is null)
        {
            _day = await _database.GetOrCreateClassDayAsync(_context.CurrentClass.Id, DateTime.Today);
            _items = (await _database.GetDailyEventItemsAsync(
                _day.Id,
                _selectedEvent.Event.Id,
                activeMembersOnly: true)).ToList();
        }
        return personId is null
            ? null
            : _items.FirstOrDefault(item => item.DayPerson.PersonId == personId.Value);
    }

    private static Color StatusColor(EventStatusSemantic? semantic) => semantic switch
    {
        EventStatusSemantic.Positive => Color.FromArgb("#DCFCE7"),
        EventStatusSemantic.Negative => Color.FromArgb("#FEE2E2"),
        EventStatusSemantic.Excused => Color.FromArgb("#FEF3C7"),
        EventStatusSemantic.Neutral => Color.FromArgb("#DBEAFE"),
        _ => Color.FromArgb("#E2E8F0")
    };
}

using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class ReviewPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly MotionService _motion;
    private readonly int _classDayId;
    private readonly EventDefinition _event;
    private readonly IReadOnlyList<EventStatusOption> _options;
    private readonly bool _unresolvedOnly;
    private readonly bool _activeMembersOnly;
    private readonly string _subjectLabel;
    private readonly Label _progress = Ui.Secondary();
    private readonly Label _name = Ui.Heading(string.Empty, 34);
    private readonly Label _state = Ui.Secondary();
    private readonly VerticalStackLayout _statuses = new() { Spacing = 8 };
    private readonly Button _note = Ui.SecondaryButton(string.Empty);
    private readonly Button _previous = Ui.SecondaryButton(string.Empty);
    private readonly Button _next = Ui.SecondaryButton(string.Empty);
    private readonly Border _personCard;
    private List<DailyEventItem> _items = [];
    private int _index;
    private bool _transitioning;

    public ReviewPage(
        DatabaseService database,
        MotionService motion,
        int classDayId,
        EventDefinition definition,
        IReadOnlyList<EventStatusOption> options,
        string subjectLabel,
        bool unresolvedOnly,
        bool activeMembersOnly = false)
    {
        _database = database;
        _motion = motion;
        _classDayId = classDayId;
        _event = definition;
        _options = options;
        _unresolvedOnly = unresolvedOnly;
        _activeMembersOnly = activeMembersOnly;
        _subjectLabel = subjectLabel;
        Title = definition.Name;
        Padding = new Thickness(20);
        _name.HorizontalTextAlignment = TextAlignment.Center;
        _state.HorizontalTextAlignment = TextAlignment.Center;
        foreach (var option in options)
        {
            _statuses.Add(CreateStatusButton(option));
        }
        var clear = Ui.SecondaryButton(LocalizationService.T("Unknown"));
        clear.Clicked += async (_, _) => await SetStatusAsync(0);
        _statuses.Add(clear);
        _note.Clicked += NoteClicked;
        _previous.Text = LocalizationService.T("Back");
        _next.Text = LocalizationService.T("Next");
        _previous.Clicked += async (_, _) => await MoveAsync(-1);
        _next.Clicked += async (_, _) => await MoveAsync(1);
        var nav = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        nav.Add(_previous, 0, 0);
        nav.Add(_next, 1, 0);
        _personCard = Ui.Card(new VerticalStackLayout
        {
            Spacing = 12,
            Children = { _name, _state, _note }
        }, new Thickness(20));
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    _progress,
                    _personCard,
                    _statuses,
                    nav
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadAsync);
    }

    private async Task LoadAsync()
    {
        var all = (await _database.GetDailyEventItemsAsync(
            _classDayId,
            _event.Id,
            _activeMembersOnly)).ToList();
        _items = _unresolvedOnly
            ? all.Where(i => i.StatusOptionId == 0 && (!_event.UseExpectedRoster || i.IsExpected)).ToList()
            : all;
        _index = 0;
        Render();
    }

    private Button CreateStatusButton(EventStatusOption option)
    {
        var button = new Button
        {
            Text = $"{option.Symbol} {option.Label}",
            MinimumHeightRequest = 54,
            BackgroundColor = option.Semantic switch
            {
                EventStatusSemantic.Positive => Color.FromArgb("#DCFCE7"),
                EventStatusSemantic.Negative => Color.FromArgb("#FEE2E2"),
                EventStatusSemantic.Excused => Color.FromArgb("#FEF3C7"),
                _ => Color.FromArgb("#DBEAFE")
            },
            TextColor = Color.FromArgb("#0F172A")
        };
        button.Clicked += async (_, _) => await SetStatusAsync(option.Id);
        return button;
    }

    private async Task SetStatusAsync(int optionId)
    {
        if (_items.Count == 0 || _transitioning)
        {
            return;
        }
        var item = _items[_index];
        await _database.SetEventStatusAsync(item.Record.Id, optionId);
        item.Record.StatusOptionId = optionId;
        if (_index < _items.Count - 1)
        {
            await MoveAsync(1);
            return;
        }
        await DisplayAlertAsync(
            LocalizationService.T("ReviewComplete"),
            LocalizationService.T("ReviewCompleteMessage"),
            LocalizationService.T("OK"));
        await Navigation.PopAsync();
    }

    private async void NoteClicked(object? sender, EventArgs e)
    {
        if (_items.Count == 0 || _transitioning)
        {
            return;
        }
        var item = _items[_index];
        var note = await DisplayPromptAsync(
            string.Format(LocalizationService.T("SubjectNote"), _subjectLabel),
            item.Name,
            initialValue: item.Note,
            maxLength: 2000,
            keyboard: Keyboard.Text);
        if (note is null)
        {
            return;
        }
        await _database.SetEventNoteAsync(item.Record.Id, note);
        item.Record.Note = note.Trim();
        Render();
    }

    private async Task MoveAsync(int delta)
    {
        if (_items.Count == 0 || _transitioning)
        {
            return;
        }
        var nextIndex = Math.Clamp(_index + delta, 0, _items.Count - 1);
        if (nextIndex == _index)
        {
            return;
        }
        _transitioning = true;
        Render();
        try
        {
            await _motion.ChangeStepAsync(_personCard, delta, () =>
            {
                _index = nextIndex;
                Render();
            });
        }
        finally
        {
            _transitioning = false;
            Render();
        }
    }

    private void Render()
    {
        if (_items.Count == 0)
        {
            _progress.Text = string.Empty;
            _name.Text = LocalizationService.T("NothingToReview");
            _state.Text = LocalizationService.T("AllReviewed");
            _note.IsEnabled = _previous.IsEnabled = _next.IsEnabled = false;
            _statuses.IsEnabled = false;
            return;
        }
        var item = _items[_index];
        var selected = _options.FirstOrDefault(o => o.Id == item.StatusOptionId);
        _progress.Text = string.Format(
            LocalizationService.T("SubjectProgress"),
            _subjectLabel,
            _index + 1,
            _items.Count);
        _name.Text = item.Name + (item.IsExpected ? string.Empty : " ·");
        _state.Text = $"{LocalizationService.T("CurrentStatus")}: {selected?.Label ?? LocalizationService.T("Unknown")}";
        _note.Text = string.IsNullOrWhiteSpace(item.Note)
            ? LocalizationService.T("AddNote")
            : LocalizationService.T("EditNote");
        _note.IsEnabled = !_transitioning;
        _statuses.IsEnabled = !_transitioning;
        _previous.IsEnabled = !_transitioning && _index > 0;
        _next.IsEnabled = !_transitioning && _index < _items.Count - 1;
    }
}

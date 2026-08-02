using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class EventEditPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassGroup _classGroup;
    private readonly EventDefinition _event;
    private readonly ReminderCoordinator _reminders;
    private readonly Entry _name = new();
    private readonly Entry _positiveSymbol = new();
    private readonly Switch _expectedRoster = new();
    private readonly VerticalStackLayout _statusRows = new() { Spacing = 8 };
    private readonly Entry _newLabel = new();
    private readonly Entry _newSymbol = new();
    private readonly Picker _newSemantic = new();

    public EventEditPage(
        DatabaseService database,
        ClassGroup classGroup,
        EventDefinition definition,
        ReminderCoordinator reminders)
    {
        _database = database;
        _classGroup = classGroup;
        _event = definition;
        _reminders = reminders;
        Title = definition.Name;
        Padding = new Thickness(16);
        _name.Text = definition.Name;
        _name.Placeholder = LocalizationService.T("EventName");
        _positiveSymbol.Text = definition.PositiveSymbol;
        _positiveSymbol.Placeholder = LocalizationService.T("PositiveSymbol");
        _expectedRoster.IsToggled = definition.UseExpectedRoster;
        var save = new Button { Text = LocalizationService.T("Save") };
        save.Clicked += SaveClicked;
        var reminderButton = Ui.SecondaryButton(LocalizationService.T("Reminders"));
        reminderButton.Clicked += async (_, _) => await Navigation.PushAsync(new RemindersPage(
            _database, _classGroup, _event, _reminders));
        _newLabel.Placeholder = LocalizationService.T("StatusLabel");
        _newSymbol.Placeholder = LocalizationService.T("Symbol");
        _newSemantic.ItemsSource = Enum.GetValues<EventStatusSemantic>()
            .Select(SemanticText)
            .ToList();
        _newSemantic.SelectedIndex = 2;
        var addStatus = Ui.SecondaryButton(LocalizationService.T("AddStatus"));
        addStatus.Clicked += AddStatusClicked;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            Ui.Secondary(LocalizationService.T("EventName")), _name,
                            Ui.Secondary(LocalizationService.T("PositiveSymbol")), _positiveSymbol,
                            SwitchRow(LocalizationService.T("UseExpectedRoster"), _expectedRoster),
                            save, reminderButton
                        }
                    }),
                    Ui.Heading(LocalizationService.T("Statuses"), 18),
                    _statusRows,
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children = { _newLabel, _newSymbol, _newSemantic, addStatus }
                    })
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadStatusesAsync);
    }

    private async Task LoadStatusesAsync()
    {
        _statusRows.Clear();
        var options = await _database.GetEventStatusOptionsAsync(_event.Id);
        foreach (var option in options)
        {
            var label = new Entry { Text = option.Label };
            var symbol = new Entry { Text = option.Symbol, WidthRequest = 58 };
            var semantic = new Picker
            {
                ItemsSource = Enum.GetValues<EventStatusSemantic>().Select(SemanticText).ToList(),
                SelectedIndex = Array.IndexOf(Enum.GetValues<EventStatusSemantic>(), option.Semantic)
            };
            var save = Ui.SecondaryButton(LocalizationService.T("Save"));
            save.Clicked += async (_, _) =>
            {
                option.Label = label.Text ?? string.Empty;
                option.Symbol = symbol.Text ?? string.Empty;
                option.Semantic = Enum.GetValues<EventStatusSemantic>()[Math.Max(0, semantic.SelectedIndex)];
                await _database.UpdateStatusOptionAsync(option);
                await LoadStatusesAsync();
            };
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                ColumnSpacing = 6,
                RowSpacing = 6
            };
            row.Add(label, 0, 0);
            row.Add(symbol, 1, 0);
            row.Add(semantic, 0, 1);
            row.Add(save, 1, 1);
            _statusRows.Add(Ui.Card(row, new Thickness(8, 4)));
        }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        var oldName = _event.Name;
        try
        {
            _event.Name = _name.Text ?? string.Empty;
            _event.PositiveSymbol = _positiveSymbol.Text ?? string.Empty;
            _event.UseExpectedRoster = _expectedRoster.IsToggled;
            await _database.UpdateEventAsync(_event);
            Title = _event.Name;
        }
        catch (Exception exception)
        {
            _event.Name = oldName;
            _name.Text = oldName;
            await DisplayAlertAsync(LocalizationService.T("Error"), exception.Message, LocalizationService.T("OK"));
        }
    }

    private async void AddStatusClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_newLabel.Text))
        {
            return;
        }
        var semantic = Enum.GetValues<EventStatusSemantic>()[Math.Max(0, _newSemantic.SelectedIndex)];
        await _database.AddStatusOptionAsync(_event.Id, _newLabel.Text, _newSymbol.Text ?? string.Empty, semantic);
        _newLabel.Text = _newSymbol.Text = string.Empty;
        await LoadStatusesAsync();
    }

    private static HorizontalStackLayout SwitchRow(string text, Switch value) => new()
    {
        Spacing = 8,
        Children = { value, new Label { Text = text, VerticalTextAlignment = TextAlignment.Center } }
    };

    private static string SemanticText(EventStatusSemantic semantic) => semantic switch
    {
        EventStatusSemantic.Positive => LocalizationService.T("PositiveStatus"),
        EventStatusSemantic.Negative => LocalizationService.T("NegativeStatus"),
        EventStatusSemantic.Excused => LocalizationService.T("Excused"),
        _ => LocalizationService.T("NeutralStatus")
    };
}

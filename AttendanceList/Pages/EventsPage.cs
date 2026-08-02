using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;
using Microsoft.Maui.Layouts;

namespace AttendanceList.Pages;

public sealed class EventsPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassContextService _context;
    private readonly ReminderCoordinator _reminders;
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };

    public EventsPage(DatabaseService database, ClassContextService context, ReminderCoordinator reminders)
    {
        _database = database;
        _context = context;
        _reminders = reminders;
        Title = LocalizationService.T("EventManagement");
        Padding = new Thickness(16);
        var add = new Button { Text = LocalizationService.T("AddEvent") };
        add.Clicked += AddClicked;
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 12
        };
        root.Add(add, 0, 0);
        root.Add(new ScrollView { Content = _rows }, 0, 1);
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
        _rows.Clear();
        if (_context.CurrentClass is null)
        {
            return;
        }
        var events = await _database.GetEventsForClassAsync(_context.CurrentClass.Id, includeDisabled: true);
        foreach (var definition in events)
        {
            var enabled = new Switch { IsToggled = definition.IsEnabled };
            SemanticProperties.SetDescription(
                enabled,
                $"{definition.Name}: {LocalizationService.T("Enabled")}");
            enabled.Toggled += async (_, args) =>
            {
                await _database.SetClassEventEnabledAsync(
                    _context.CurrentClass.Id,
                    definition.Event.Id,
                    args.Value);
                await _reminders.RescheduleAsync(requestPermission: false);
            };
            var edit = Ui.SecondaryButton(LocalizationService.T("Edit"));
            edit.Clicked += async (_, _) => await Navigation.PushAsync(new EventEditPage(
                _database,
                _context.CurrentClass,
                definition.Event,
                _reminders));
            var reminders = Ui.SecondaryButton(LocalizationService.T("Reminders"));
            reminders.Clicked += async (_, _) => await Navigation.PushAsync(new RemindersPage(
                _database,
                _context.CurrentClass,
                definition.Event,
                _reminders));
            var actions = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                AlignItems = FlexAlignItems.Center,
                JustifyContent = FlexJustify.End
            };
            actions.Children.Add(enabled);
            actions.Children.Add(edit);
            actions.Children.Add(reminders);
            var row = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                RowSpacing = 6
            };
            row.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    Ui.Heading(definition.Name, 17),
                    Ui.Secondary(string.Join(" · ", definition.StatusOptions.Select(o => o.Label)))
                }
            }, 0, 0);
            row.Add(actions, 0, 1);
            _rows.Add(Ui.Card(row, new Thickness(12, 8)));
        }
    }

    private async void AddClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentOrganization is null || _context.CurrentClass is null)
        {
            return;
        }
        var name = await DisplayPromptAsync(
            LocalizationService.T("AddEvent"),
            LocalizationService.T("EventName"),
            placeholder: LocalizationService.T("EventExample"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var positive = await DisplayPromptAsync(
            LocalizationService.T("PositiveStatus"),
            LocalizationService.T("StatusLabel"),
            placeholder: LocalizationService.T("PositiveStatusExample"));
        if (string.IsNullOrWhiteSpace(positive))
        {
            return;
        }
        var negative = await DisplayPromptAsync(
            LocalizationService.T("NegativeStatus"),
            LocalizationService.T("StatusLabel"),
            placeholder: LocalizationService.T("NegativeStatusExample"));
        if (string.IsNullOrWhiteSpace(negative))
        {
            return;
        }
        try
        {
            await _database.AddEventAsync(
                _context.CurrentOrganization.Id,
                _context.CurrentClass.Id,
                name,
                positive,
                negative);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(LocalizationService.T("Error"), exception.Message, LocalizationService.T("OK"));
        }
    }
}

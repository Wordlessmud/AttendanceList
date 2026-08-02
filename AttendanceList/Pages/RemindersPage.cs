using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;
using Microsoft.Maui.Layouts;

namespace AttendanceList.Pages;

public sealed class RemindersPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassGroup _classGroup;
    private readonly EventDefinition _event;
    private readonly ReminderCoordinator _coordinator;
    private readonly TimePicker _time = new() { Time = new TimeSpan(9, 0, 0) };
    private readonly Entry _message = new();
    private readonly Switch _operatingOnly = new() { IsToggled = true };
    private readonly Switch _incompleteOnly = new() { IsToggled = true };
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };

    public RemindersPage(
        DatabaseService database,
        ClassGroup classGroup,
        EventDefinition definition,
        ReminderCoordinator coordinator)
    {
        _database = database;
        _classGroup = classGroup;
        _event = definition;
        _coordinator = coordinator;
        Title = $"{definition.Name} · {LocalizationService.T("Reminders")}";
        Padding = new Thickness(16);
        _message.Placeholder = LocalizationService.T("ReminderMessageOptional");
        var add = new Button { Text = LocalizationService.T("AddReminder") };
        add.Clicked += AddClicked;
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
                            Ui.Secondary(LocalizationService.T("ReminderTime")), _time, _message,
                            SwitchRow(LocalizationService.T("OnlyOperatingDays"), _operatingOnly),
                            SwitchRow(LocalizationService.T("OnlyIfIncomplete"), _incompleteOnly),
                            add,
                            Ui.Secondary(LocalizationService.T("ReminderAccuracyHint"))
                        }
                    }),
                    _rows
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
        _rows.Clear();
        var reminders = await _database.GetEventRemindersAsync(_classGroup.Id, _event.Id);
        foreach (var reminder in reminders)
        {
            var enabled = new Switch { IsToggled = reminder.IsEnabled };
            enabled.Toggled += async (_, args) =>
            {
                reminder.IsEnabled = args.Value;
                await SaveAndScheduleAsync(reminder, requestPermission: args.Value);
            };
            var days = Ui.SecondaryButton(LocalizationService.T("Days"));
            days.Clicked += async (_, _) => await Navigation.PushAsync(new WeekdayMaskPage(
                LocalizationService.T("ReminderDays"),
                reminder.DaysMask,
                async mask =>
                {
                    reminder.DaysMask = mask;
                    await SaveAndScheduleAsync(reminder, requestPermission: false);
                }));
            var edit = Ui.SecondaryButton(LocalizationService.T("Edit"));
            edit.Clicked += async (_, _) => await EditAsync(reminder);
            var delete = Ui.SecondaryButton(LocalizationService.T("Delete"));
            delete.Clicked += async (_, _) =>
            {
                var confirmed = await DisplayAlertAsync(
                    LocalizationService.T("DeleteReminderTitle"),
                    string.Format(
                        LocalizationService.T("DeleteReminderMessage"),
                        TimeSpan.FromMinutes(reminder.TimeMinutes).ToString(@"hh\:mm")),
                    LocalizationService.T("Delete"),
                    LocalizationService.T("Cancel"));
                if (!confirmed)
                {
                    return;
                }
                await _database.DeleteEventReminderAsync(reminder);
                await _coordinator.RescheduleAsync(requestPermission: false);
                await LoadAsync();
            };
            var actions = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                AlignItems = FlexAlignItems.Center,
                JustifyContent = FlexJustify.End
            };
            actions.Children.Add(enabled);
            actions.Children.Add(days);
            actions.Children.Add(edit);
            actions.Children.Add(delete);
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
                    Ui.Heading(TimeSpan.FromMinutes(reminder.TimeMinutes).ToString(@"hh\:mm"), 17),
                    Ui.Secondary(string.IsNullOrWhiteSpace(reminder.Message)
                        ? LocalizationService.T("ReminderDefaultMessage")
                        : reminder.Message)
                }
            }, 0, 0);
            row.Add(actions, 0, 1);
            _rows.Add(Ui.Card(row, new Thickness(10, 6)));
        }
    }

    private async void AddClicked(object? sender, EventArgs e)
    {
        var time = _time.Time ?? new TimeSpan(9, 0, 0);
        var reminder = new EventReminder
        {
            ClassGroupId = _classGroup.Id,
            EventDefinitionId = _event.Id,
            TimeMinutes = (int)time.TotalMinutes,
            DaysMask = _classGroup.OperatingDaysMask,
            OnlyOperatingDays = _operatingOnly.IsToggled,
            OnlyIfIncomplete = _incompleteOnly.IsToggled,
            Message = _message.Text ?? string.Empty
        };
        await SaveAndScheduleAsync(reminder, requestPermission: true);
        _message.Text = string.Empty;
        await LoadAsync();
    }

    private async Task EditAsync(EventReminder reminder)
    {
        var timeText = await DisplayPromptAsync(
            LocalizationService.T("ReminderTime"),
            LocalizationService.T("TimeFormatHint"),
            initialValue: TimeSpan.FromMinutes(reminder.TimeMinutes).ToString(@"hh\:mm"));
        if (timeText is not null && TimeSpan.TryParse(timeText, out var time))
        {
            reminder.TimeMinutes = (int)Math.Clamp(time.TotalMinutes, 0, 1439);
        }
        var message = await DisplayPromptAsync(
            LocalizationService.T("ReminderMessageOptional"),
            _event.Name,
            initialValue: reminder.Message,
            maxLength: 240);
        if (message is not null)
        {
            reminder.Message = message;
        }
        await SaveAndScheduleAsync(reminder, requestPermission: false);
        await LoadAsync();
    }

    private async Task SaveAndScheduleAsync(EventReminder reminder, bool requestPermission)
    {
        var allowed = await _coordinator.SaveAndRescheduleAsync(reminder, requestPermission);
        if (!allowed && (requestPermission || _coordinator.LastErrorMessage is not null))
        {
            var message = _coordinator.LastErrorMessage is null
                ? LocalizationService.T("ReminderSavedDisabled")
                : string.Format(
                    LocalizationService.T("ReminderSavedDisabledWithReason"),
                    _coordinator.LastErrorMessage);
            await DisplayAlertAsync(
                _coordinator.LastErrorMessage is null
                    ? LocalizationService.T("NotificationsDisabled")
                    : LocalizationService.T("Error"),
                message,
                LocalizationService.T("OK"));
            await LoadAsync();
        }
    }

    private static HorizontalStackLayout SwitchRow(string text, Switch value) => new()
    {
        Spacing = 8,
        Children = { value, new Label { Text = text, VerticalTextAlignment = TextAlignment.Center } }
    };
}

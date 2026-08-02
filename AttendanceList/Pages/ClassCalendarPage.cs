using System.Collections.ObjectModel;
using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class ClassCalendarPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassGroup _classGroup;
    private readonly DatePicker _date = new() { Date = DateTime.Today };
    private readonly Picker _kind = new();
    private readonly Entry _label = new();
    private readonly ObservableCollection<DayOverride> _rows = [];

    public ClassCalendarPage(DatabaseService database, ClassGroup classGroup)
    {
        _database = database;
        _classGroup = classGroup;
        Title = LocalizationService.T("CalendarOverrides");
        Padding = new Thickness(16);
        _date.Format = LocalizationService.DatePickerFormat;
        _kind.ItemsSource = new[]
        {
            LocalizationService.T("ExcludedDay"),
            LocalizationService.T("IncludedDay"),
            LocalizationService.T("SpecialDay")
        };
        _kind.SelectedIndex = 2;
        _label.Placeholder = LocalizationService.T("DayLabel");

        var save = new Button { Text = LocalizationService.T("Save") };
        save.Clicked += SaveClicked;
        var delete = Ui.SecondaryButton(LocalizationService.T("RemoveOverride"));
        delete.Clicked += DeleteClicked;
        var form = Ui.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _date, _kind, _label, new HorizontalStackLayout { Spacing = 8, Children = { save, delete } } }
        });

        var collection = new CollectionView
        {
            ItemsSource = _rows,
            SelectionMode = SelectionMode.Single,
            EmptyView = Ui.Secondary(LocalizationService.T("NoOverrides")),
            ItemTemplate = new DataTemplate(() =>
            {
                var label = Ui.Secondary();
                label.SetBinding(Label.TextProperty, new Binding(path: ".", converter: new OverrideTextConverter()));
                return Ui.Card(label, new Thickness(12, 8));
            })
        };
        collection.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not DayOverride value)
            {
                return;
            }
            collection.SelectedItem = null;
            _date.Date = DateTime.ParseExact(value.DateKey, "yyyy-MM-dd", null);
            _kind.SelectedIndex = value.Kind switch
            {
                DayOverrideKind.Excluded => 0,
                DayOverrideKind.Included => 1,
                _ => 2
            };
            _label.Text = value.Label;
        };

        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            RowSpacing = 12
        };
        root.Add(form, 0, 0);
        root.Add(collection, 0, 1);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadAsync);
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        var kind = _kind.SelectedIndex switch
        {
            0 => DayOverrideKind.Excluded,
            1 => DayOverrideKind.Included,
            _ => DayOverrideKind.Special
        };
        await _database.SaveDayOverrideAsync(_classGroup.Id, _date.Date ?? DateTime.Today, kind, _label.Text ?? string.Empty);
        await LoadAsync();
    }

    private async void DeleteClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            LocalizationService.T("DeleteOverrideTitle"),
            string.Format(
                LocalizationService.T("DeleteOverrideMessage"),
                LocalizationService.FormatDate(_date.Date ?? DateTime.Today)),
            LocalizationService.T("Delete"),
            LocalizationService.T("Cancel"));
        if (!confirmed)
        {
            return;
        }
        await _database.DeleteDayOverrideAsync(_classGroup.Id, _date.Date ?? DateTime.Today);
        _label.Text = string.Empty;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var values = await _database.GetDayOverridesAsync(_classGroup.Id, DateTime.Today.AddYears(-2), DateTime.Today.AddYears(2));
        _rows.Clear();
        foreach (var value in values)
        {
            _rows.Add(value);
        }
    }

    private sealed class OverrideTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not DayOverride day)
            {
                return string.Empty;
            }
            var kind = day.Kind switch
            {
                DayOverrideKind.Excluded => LocalizationService.T("ExcludedDay"),
                DayOverrideKind.Included => LocalizationService.T("IncludedDay"),
                _ => LocalizationService.T("SpecialDay")
            };
            return $"{LocalizationService.FormatDateKey(day.DateKey)} · {kind}{(string.IsNullOrWhiteSpace(day.Label) ? string.Empty : $" · {day.Label}")}";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

using AttendanceList.Helpers;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class WeekdayMaskPage : ContentPage
{
    private readonly Func<int, Task> _save;
    private readonly Dictionary<DayOfWeek, CheckBox> _checks = [];

    public WeekdayMaskPage(string title, int initialMask, Func<int, Task> save)
    {
        _save = save;
        Title = title;
        Padding = new Thickness(20);
        var list = new VerticalStackLayout { Spacing = 10 };
        var names = LocalizationService.CurrentCulture.DateTimeFormat.DayNames;
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var check = new CheckBox { IsChecked = DatabaseService.HasDay(initialMask, day) };
            _checks[day] = check;
            list.Add(Ui.Card(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { check, new Label { Text = names[(int)day], VerticalTextAlignment = TextAlignment.Center } }
            }, new Thickness(12, 6)));
        }
        var saveButton = new Button { Text = LocalizationService.T("Save") };
        saveButton.Clicked += SaveClicked;
        list.Add(saveButton);
        Content = new ScrollView { Content = list };
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        var mask = _checks.Where(pair => pair.Value.IsChecked).Aggregate(0, (current, pair) => current | (1 << (int)pair.Key));
        await _save(mask);
        await Navigation.PopAsync();
    }
}

using AttendanceList.Helpers;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class SettingsPage : ContentPage
{
    private bool _initialising = true;

    public SettingsPage(
        DatabaseService database,
        ClassContextService context,
        ReminderCoordinator reminders)
    {
        Title = LocalizationService.T("Settings");
        Padding = new Thickness(16);

        var languagePicker = new Picker
        {
            Title = LocalizationService.T("Language"),
            ItemsSource = new[]
            {
                LocalizationService.T("SystemDefault"),
                "English",
                "简体中文"
            }
        };
        languagePicker.SelectedIndex = LocalizationService.CurrentLanguageMode switch
        {
            LocalizationService.SystemLanguageMode => 0,
            LocalizationService.SimplifiedChineseLanguage => 2,
            _ => 1
        };
        languagePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (_initialising || languagePicker.SelectedIndex < 0)
            {
                return;
            }
            languagePicker.IsEnabled = false;
            var restarted = false;
            try
            {
                var languageMode = languagePicker.SelectedIndex switch
                {
                    0 => LocalizationService.SystemLanguageMode,
                    2 => LocalizationService.SimplifiedChineseLanguage,
                    _ => LocalizationService.EnglishLanguage
                };
                LocalizationService.SetLanguageMode(languageMode);
                await database.LocalizeBuiltInDefaultsAsync();
                await context.RefreshAsync();
                await reminders.RescheduleAsync(requestPermission: false);
                if (Application.Current is App app)
                {
                    await app.RestartUiAsync();
                    restarted = true;
                }
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync(
                    LocalizationService.T("Error"),
                    exception.Message,
                    LocalizationService.T("OK"));
            }
            finally
            {
                if (!restarted)
                {
                    languagePicker.IsEnabled = true;
                }
            }
        };

        var dateFormatPicker = new Picker
        {
            Title = LocalizationService.T("DateFormat"),
            ItemsSource = new[]
            {
                LocalizationService.T("DateFormatSystem"),
                LocalizationService.T("DateFormatDayMonthYear"),
                LocalizationService.T("DateFormatMonthDayYear"),
                LocalizationService.T("DateFormatYearMonthDay")
            }
        };
        dateFormatPicker.SelectedIndex = LocalizationService.CurrentDateFormatMode switch
        {
            LocalizationService.DayMonthYearDateFormatMode => 1,
            LocalizationService.MonthDayYearDateFormatMode => 2,
            LocalizationService.YearMonthDayDateFormatMode => 3,
            _ => 0
        };
        dateFormatPicker.SelectedIndexChanged += async (_, _) =>
        {
            if (_initialising || dateFormatPicker.SelectedIndex < 0)
            {
                return;
            }

            dateFormatPicker.IsEnabled = false;
            var dateFormatMode = dateFormatPicker.SelectedIndex switch
            {
                1 => LocalizationService.DayMonthYearDateFormatMode,
                2 => LocalizationService.MonthDayYearDateFormatMode,
                3 => LocalizationService.YearMonthDayDateFormatMode,
                _ => LocalizationService.SystemDateFormatMode
            };
            LocalizationService.SetDateFormatMode(dateFormatMode);
            if (Application.Current is App app)
            {
                await app.RestartUiAsync();
            }
            else
            {
                dateFormatPicker.IsEnabled = true;
            }
        };

        var dateFormatExample = string.Format(
            LocalizationService.CurrentCulture,
            LocalizationService.T("DateFormatDescription"),
            LocalizationService.FormatDate(new DateTime(2026, 8, 2)));

        var buttonSizePicker = new Picker
        {
            Title = LocalizationService.T("ButtonSize"),
            ItemsSource = new[]
            {
                LocalizationService.T("Small"),
                LocalizationService.T("Medium"),
                LocalizationService.T("Large")
            }
        };
        var savedSize = Preferences.Default.Get("button_size", "medium");
        buttonSizePicker.SelectedIndex = savedSize switch { "small" => 0, "large" => 2, _ => 1 };
        buttonSizePicker.SelectedIndexChanged += (_, _) =>
        {
            if (buttonSizePicker.SelectedIndex < 0)
            {
                return;
            }
            var value = buttonSizePicker.SelectedIndex switch { 0 => "small", 2 => "large", _ => "medium" };
            Preferences.Default.Set("button_size", value);
            if (Application.Current is App app)
            {
                app.ApplyButtonSize();
            }
        };

        var reduceMotion = new Switch
        {
            IsToggled = Preferences.Default.Get("reduce_motion", false)
        };
        reduceMotion.Toggled += (_, args) =>
            Preferences.Default.Set("reduce_motion", args.Value);
        var reduceMotionRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        reduceMotionRow.Add(new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                Ui.Heading(LocalizationService.T("ReduceMotion"), 16),
                Ui.Secondary(LocalizationService.T("ReduceMotionDescription"))
            }
        }, 0, 0);
        reduceMotionRow.Add(reduceMotion, 1, 0);

        var localOnly = Ui.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Ui.Heading(LocalizationService.T("LocalData"), 18),
                Ui.Secondary(LocalizationService.T("LocalDataDescription"))
            }
        });

        var showGuide = Ui.SecondaryButton(LocalizationService.T("ShowOnboardingAgain"));
        showGuide.Clicked += async (_, _) =>
        {
            OnboardingService.Reset();
            if (Application.Current is App app)
            {
                await app.RestartUiAsync();
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children = { Ui.Secondary(LocalizationService.T("Language")), languagePicker }
                    }),
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            Ui.Secondary(LocalizationService.T("DateFormat")),
                            dateFormatPicker,
                            Ui.Secondary(dateFormatExample)
                        }
                    }),
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children = { Ui.Secondary(LocalizationService.T("ButtonSize")), buttonSizePicker }
                    }),
                    Ui.Card(reduceMotionRow),
                    localOnly,
                    showGuide,
                    Ui.Secondary(LocalizationService.T("VersionText")),
                    Ui.Watermark(LocalizationService.T("WatermarkText"))
                }
            }
        };

        _initialising = false;
    }
}

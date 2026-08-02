using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class OnboardingPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassContextService _context;
    private readonly ReminderCoordinator _reminders;
    private readonly MotionService _motion;
    private readonly Label _progress = Ui.Secondary();
    private readonly Label _title = Ui.Heading(string.Empty, 30);
    private readonly Label _body = new() { FontSize = 17, LineHeight = 1.35 };
    private readonly VerticalStackLayout _interactive = new() { Spacing = 10 };
    private readonly Grid _stepContent;
    private readonly Button _back = Ui.SecondaryButton(string.Empty);
    private readonly Button _next = new();
    private readonly Button _skip = Ui.SecondaryButton(string.Empty);
    private readonly Picker _languagePicker = new();
    private readonly Entry _organizationName = new();
    private readonly Entry _className = new();
    private readonly Entry _firstPersonName = new();
    private readonly Button _addFirstPerson = Ui.SecondaryButton(string.Empty);
    private readonly Label _firstPersonFeedback = Ui.Secondary();
    private ClassMember? _onboardingMember;
    private int _index;
    private bool _loaded;
    private bool _changingLanguage;

    private readonly (string TitleKey, string BodyKey)[] _steps =
    [
        ("OnboardingWelcomeTitle", "OnboardingWelcomeBody"),
        ("OnboardingClassTitle", "OnboardingClassBody"),
        ("OnboardingEventTitle", "OnboardingEventBody"),
        ("OnboardingReminderTitle", "OnboardingReminderBody"),
        ("OnboardingExportTitle", "OnboardingExportBody")
    ];

    public OnboardingPage(
        DatabaseService database,
        ClassContextService context,
        ReminderCoordinator reminders,
        MotionService motion)
    {
        _database = database;
        _context = context;
        _reminders = reminders;
        _motion = motion;
        Padding = new Thickness(24);
        _body.SetAppThemeColor(Label.TextColorProperty, Ui.TextPrimaryLight, Ui.TextPrimaryDark);
        _body.VerticalOptions = LayoutOptions.Center;
        _languagePicker.ItemsSource = new[] { "English", "简体中文" };
        _languagePicker.WidthRequest = 130;
        _languagePicker.SelectedIndex = LocalizationService.CurrentLanguage == "zh-CN" ? 1 : 0;
        _languagePicker.IsEnabled = false;
        _languagePicker.SelectedIndexChanged += LanguageChanged;
        _addFirstPerson.Clicked += AddFirstPersonClicked;
        _firstPersonFeedback.IsVisible = false;
        ApplyLocalizedText();
        _back.Clicked += async (_, _) => await MoveAsync(-1);
        _next.Clicked += async (_, _) => await MoveAsync(1);
        _skip.Clicked += async (_, _) => await FinishAsync();
        var navigation = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        navigation.Add(_back, 0, 0);
        navigation.Add(_skip, 1, 0);
        navigation.Add(_next, 2, 0);
        _stepContent = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 18
        };
        _stepContent.Add(_title, 0, 0);
        _stepContent.Add(_body, 0, 1);
        _stepContent.Add(_interactive, 0, 2);
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 18
        };
        var topBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        topBar.Add(_progress, 0, 0);
        topBar.Add(_languagePicker, 1, 0);
        root.Add(topBar, 0, 0);
        root.Add(new ScrollView { Content = _stepContent }, 0, 1);
        root.Add(navigation, 0, 2);
        Content = Ui.Card(root, new Thickness(26));
        Render();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadAsync);
    }

    private async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }
        await _context.EnsureInitializedAsync();
        _organizationName.Text = _context.CurrentOrganization?.Name;
        _className.Text = _context.CurrentClass?.Name;
        _loaded = true;
        _languagePicker.IsEnabled = true;
        Render();
    }

    private async Task MoveAsync(int delta)
    {
        if (delta > 0 && _index == 1)
        {
            await SaveContextNamesAsync();
        }
        if (delta > 0 && _index == _steps.Length - 1)
        {
            await FinishAsync();
            return;
        }
        _index = Math.Clamp(_index + delta, 0, _steps.Length - 1);
        Render();
        await _motion.EnterStepAsync(_stepContent, delta);
    }

    private void Render()
    {
        _progress.Text = string.Format(LocalizationService.T("GuideProgress"), _index + 1, _steps.Length);
        _title.Text = LocalizationService.T(_steps[_index].TitleKey);
        _body.Text = LocalizationService.T(_steps[_index].BodyKey);
        _back.IsVisible = _index > 0;
        _skip.IsVisible = _index < _steps.Length - 1;
        _next.Text = _index == _steps.Length - 1
            ? LocalizationService.T("StartUsing")
            : LocalizationService.T("Next");
        _interactive.Clear();
        if (_index == 1)
        {
            _interactive.Add(_organizationName);
            _interactive.Add(_className);
            _interactive.Add(Ui.Secondary(LocalizationService.T("OnboardingFirstPersonHelp")));
            _interactive.Add(_firstPersonName);
            _interactive.Add(_addFirstPerson);
            _interactive.Add(_firstPersonFeedback);
        }
        else if (_index == 2)
        {
            _interactive.Add(Ui.Card(new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    Ui.Heading(LocalizationService.T("Attendance"), 18),
                    Ui.Secondary($"{LocalizationService.T("Present")} / {LocalizationService.T("Absent")} / {LocalizationService.T("Excused")}"),
                    Ui.Heading(LocalizationService.T("Meals"), 18),
                    Ui.Secondary($"{LocalizationService.T("Ate")} / {LocalizationService.T("DidNotEat")}"),
                    Ui.Secondary(LocalizationService.T("OnboardingCustomEventExample"))
                }
            }, new Thickness(14)));
        }
        else if (_index == 3)
        {
            var permission = new Button { Text = LocalizationService.T("EnableNotifications") };
            permission.Clicked += async (_, _) =>
            {
                var allowed = await _reminders.RescheduleAsync(requestPermission: true);
                permission.Text = allowed
                    ? LocalizationService.T("NotificationsEnabled")
                    : LocalizationService.T("NotificationsDisabled");
            };
            _interactive.Add(permission);
        }
    }

    private async Task SaveContextNamesAsync()
    {
        if (_context.CurrentOrganization is not null && !string.IsNullOrWhiteSpace(_organizationName.Text))
        {
            await _database.UpdateOrganizationNameAsync(_context.CurrentOrganization, _organizationName.Text);
        }
        if (_context.CurrentClass is not null && !string.IsNullOrWhiteSpace(_className.Text))
        {
            _context.CurrentClass.Name = _className.Text;
            await _database.UpdateClassAsync(_context.CurrentClass);
        }
        if (_context.CurrentClass is not null && !string.IsNullOrWhiteSpace(_firstPersonName.Text))
        {
            if (_onboardingMember is null)
            {
                _onboardingMember = await _database.AddPersonToClassAsync(
                    _context.CurrentClass.Id,
                    _firstPersonName.Text);
            }
            else if (!string.Equals(
                         _onboardingMember.DisplayName,
                         _firstPersonName.Text.Trim(),
                         StringComparison.Ordinal))
            {
                await _database.UpdateMemberNameAsync(_onboardingMember, _firstPersonName.Text);
            }
            _firstPersonFeedback.Text = string.Format(
                LocalizationService.T("OnboardingFirstPersonAdded"),
                _onboardingMember.DisplayName);
            _firstPersonFeedback.IsVisible = true;
        }
    }

    private async void AddFirstPersonClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_firstPersonName.Text))
        {
            await DisplayAlertAsync(
                LocalizationService.T("FirstPersonNameRequiredTitle"),
                LocalizationService.T("FirstPersonNameRequiredMessage"),
                LocalizationService.T("OK"));
            return;
        }
        _addFirstPerson.IsEnabled = false;
        try
        {
            await SaveContextNamesAsync();
        }
        finally
        {
            _addFirstPerson.IsEnabled = true;
        }
    }

    private async void LanguageChanged(object? sender, EventArgs e)
    {
        if (!_loaded || _changingLanguage || _languagePicker.SelectedIndex < 0)
        {
            return;
        }
        var language = _languagePicker.SelectedIndex == 1 ? "zh-CN" : "en";
        if (language == LocalizationService.CurrentLanguage)
        {
            return;
        }
        _changingLanguage = true;
        _languagePicker.IsEnabled = false;
        var organizationWasDefault = LocalizationService.IsKnownTranslation(
            "DefaultOrganization",
            _organizationName.Text);
        var classWasDefault = LocalizationService.IsKnownTranslation("DefaultClass", _className.Text);
        try
        {
            LocalizationService.SetCulture(language);
            await _database.LocalizeBuiltInDefaultsAsync();
            await _context.RefreshAsync();
            if (organizationWasDefault)
            {
                _organizationName.Text = _context.CurrentOrganization?.Name;
            }
            if (classWasDefault)
            {
                _className.Text = _context.CurrentClass?.Name;
            }
            ApplyLocalizedText();
            Render();
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
            _changingLanguage = false;
            _languagePicker.IsEnabled = true;
        }
    }

    private void ApplyLocalizedText()
    {
        _languagePicker.Title = LocalizationService.T("Language");
        _organizationName.Placeholder = LocalizationService.T("Organization");
        _className.Placeholder = LocalizationService.T("Class");
        _firstPersonName.Placeholder = LocalizationService.T("FirstPersonPlaceholder");
        _addFirstPerson.Text = LocalizationService.T("AddFirstPerson");
        if (_onboardingMember is not null)
        {
            _firstPersonFeedback.Text = string.Format(
                LocalizationService.T("OnboardingFirstPersonAdded"),
                _onboardingMember.DisplayName);
        }
        _back.Text = LocalizationService.T("Back");
        _skip.Text = LocalizationService.T("SkipGuide");
    }

    private async Task FinishAsync()
    {
        await SaveContextNamesAsync();
        OnboardingService.Complete();
        if (Application.Current is App app)
        {
            await app.RestartUiAsync();
        }
    }
}

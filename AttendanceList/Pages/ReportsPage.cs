using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;
using Microsoft.Maui.Layouts;

namespace AttendanceList.Pages;

public sealed class ReportsPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ReportService _reports;
    private readonly ExportService _exports;
    private readonly ClassContextService _context;
    private readonly DatePicker _startPicker = new();
    private readonly DatePicker _endPicker = new();
    private readonly Picker _kindPicker = new();
    private readonly Label _classLabel = Ui.Secondary();
    private readonly Label _summaryLabel = Ui.Secondary();
    private readonly Label _qualityLabel = Ui.Secondary();
    private readonly Label _perSubjectHeading = Ui.Heading(string.Empty, 18);
    private readonly VerticalStackLayout _reportRows = new() { Spacing = 8 };
    private readonly Border _advancedSettings;

    private readonly Picker _profilePicker = new();
    private readonly Entry _profileName = new();
    private readonly Entry _baseBucketName = new();
    private readonly Entry _positiveSymbol = new();
    private readonly Picker _orientationPicker = new();
    private readonly Switch _completedOnly = new() { IsToggled = true };
    private readonly Switch _includeNotes = new() { IsToggled = true };
    private readonly Switch _includeRaw = new() { IsToggled = true };

    private readonly Entry _bucketName = new();
    private readonly Picker _bucketWeekday = new();
    private readonly Switch _bucketUseDate = new();
    private readonly DatePicker _bucketDate = new() { Date = DateTime.Today };
    private readonly Entry _bucketLabel = new();
    private readonly Switch _bucketExclude = new() { IsToggled = true };
    private readonly VerticalStackLayout _bucketRows = new() { Spacing = 6 };
    private readonly List<ExportBucketDefinition> _buckets = [];
    private List<ExportProfile> _profiles = [];
    private List<ClassEventDefinition> _events = [];
    private bool _loading;
    private int _reportRequestVersion;

    public ReportsPage(
        DatabaseService database,
        ReportService reports,
        ExportService exports,
        ClassContextService context)
    {
        _database = database;
        _reports = reports;
        _exports = exports;
        _context = context;
        Title = LocalizationService.T("Reports");
        Padding = new Thickness(16);

        _startPicker.Date = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _endPicker.Date = DateTime.Today;
        _startPicker.Format = LocalizationService.DatePickerFormat;
        _endPicker.Format = LocalizationService.DatePickerFormat;
        _bucketDate.Format = LocalizationService.DatePickerFormat;
        _kindPicker.Title = LocalizationService.T("ReportEventPicker");
        _kindPicker.ItemDisplayBinding = new Binding(nameof(ClassEventDefinition.Name));
        _kindPicker.SelectedIndexChanged += async (_, _) =>
        {
            if (!_loading)
            {
                await RunAsync();
            }
        };

        _profilePicker.Title = LocalizationService.T("SavedExportSetups");
        _profilePicker.ItemDisplayBinding = new Binding(nameof(ExportProfile.Name));
        _profileName.Placeholder = LocalizationService.T("ExportSetupName");
        _baseBucketName.Placeholder = LocalizationService.T("RegularColumnName");
        _baseBucketName.Text = LocalizationService.T("Regular");
        _positiveSymbol.Placeholder = LocalizationService.T("ExportMark");
        _positiveSymbol.Text = "✓";
        _orientationPicker.SelectedIndex = 0;

        var loadButton = Ui.SecondaryButton(LocalizationService.T("ApplySelectedSetup"));
        loadButton.Margin = new Thickness(0, 0, 8, 8);
        loadButton.Clicked += (_, _) => LoadSelectedProfile();
        var saveProfile = Ui.SecondaryButton(LocalizationService.T("SaveCurrentSetup"));
        saveProfile.Margin = new Thickness(0, 0, 8, 8);
        saveProfile.Clicked += SaveProfileClicked;
        var deleteProfile = Ui.SecondaryButton(LocalizationService.T("DeleteSelectedSetup"));
        deleteProfile.Margin = new Thickness(0, 0, 8, 8);
        deleteProfile.Clicked += DeleteProfileClicked;
        var profileActions = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center
        };
        profileActions.Children.Add(loadButton);
        profileActions.Children.Add(saveProfile);
        profileActions.Children.Add(deleteProfile);

        _bucketName.Placeholder = LocalizationService.T("SeparateTotalName");
        var weekdayNames = LocalizationService.CurrentCulture.DateTimeFormat.DayNames;
        _bucketWeekday.ItemsSource = new[] { LocalizationService.T("ChooseWeekdayOptional") }.Concat(weekdayNames).ToArray();
        _bucketWeekday.SelectedIndex = 0;
        _bucketLabel.Placeholder = LocalizationService.T("CalendarLabelOptional");
        _bucketDate.IsVisible = false;
        _bucketUseDate.Toggled += (_, args) => _bucketDate.IsVisible = args.Value;
        var addBucket = Ui.SecondaryButton(LocalizationService.T("AddSeparateTotal"));
        addBucket.Clicked += AddBucketClicked;

        var dateGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 4
        };
        dateGrid.Add(Ui.Secondary(LocalizationService.T("StartDate")), 0, 0);
        dateGrid.Add(Ui.Secondary(LocalizationService.T("EndDate")), 1, 0);
        dateGrid.Add(_startPicker, 0, 1);
        dateGrid.Add(_endPicker, 1, 1);

        var calculate = new Button { Text = LocalizationService.T("PreviewReport") };
        calculate.Clicked += async (_, _) => await RunAsync();
        var export = new Button { Text = LocalizationService.T("ExportExcel") };
        export.Clicked += ExportClicked;
        var actionRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        actionRow.Add(calculate, 0, 0);
        actionRow.Add(export, 1, 0);

        _advancedSettings = Ui.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Ui.Heading(LocalizationService.T("SavedExportSetups"), 18),
                Ui.Secondary(LocalizationService.T("SavedExportSetupsHelp")),
                _profilePicker,
                _profileName,
                profileActions,
                Ui.Heading(LocalizationService.T("WorkbookLayout"), 18),
                Ui.Secondary(LocalizationService.T("WorkbookLayoutHelp")),
                Ui.Secondary(LocalizationService.T("RegularColumnName")),
                _baseBucketName,
                Ui.Secondary(LocalizationService.T("ExportMark")),
                _positiveSymbol,
                Ui.Secondary(LocalizationService.T("TableDirection")),
                _orientationPicker,
                SwitchRow(
                    LocalizationService.T("IncludeNotesSimple"),
                    LocalizationService.T("IncludeNotesHelp"),
                    _includeNotes),
                SwitchRow(
                    LocalizationService.T("IncludeRawDataSimple"),
                    LocalizationService.T("IncludeRawDataHelp"),
                    _includeRaw),
                Ui.Heading(LocalizationService.T("SeparateTotals"), 18),
                Ui.Secondary(LocalizationService.T("SeparateTotalsHelp")),
                _bucketName,
                _bucketWeekday,
                SwitchRow(
                    LocalizationService.T("UseExactDateSimple"),
                    LocalizationService.T("UseExactDateHelp"),
                    _bucketUseDate),
                _bucketDate,
                _bucketLabel,
                SwitchRow(
                    LocalizationService.T("SubtractFromRegular"),
                    LocalizationService.T("SubtractFromRegularHelp"),
                    _bucketExclude),
                addBucket,
                Ui.Secondary(LocalizationService.T("SeparateTotalRuleHint")),
                _bucketRows
            }
        });
        _advancedSettings.IsVisible = false;
        var advancedToggle = Ui.SecondaryButton(LocalizationService.T("ShowAdvancedExportSettings"));
        advancedToggle.Clicked += (_, _) =>
        {
            _advancedSettings.IsVisible = !_advancedSettings.IsVisible;
            advancedToggle.Text = LocalizationService.T(
                _advancedSettings.IsVisible
                    ? "HideAdvancedExportSettings"
                    : "ShowAdvancedExportSettings");
        };

        var basics = Ui.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Ui.Heading(LocalizationService.T("ReportBasicsTitle"), 18),
                Ui.Secondary(LocalizationService.T("ReportBasicsHelp")),
                _classLabel,
                _kindPicker,
                dateGrid,
                SwitchRow(
                    LocalizationService.T("CompletedOnlySimple"),
                    LocalizationService.T("CompletedOnlyHelp"),
                    _completedOnly)
            }
        });
        var preview = Ui.Card(new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                Ui.Heading(LocalizationService.T("ReportPreview"), 18),
                _summaryLabel,
                _qualityLabel,
                Ui.Secondary(LocalizationService.T("DataQualityHelp"))
            }
        });

        var content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                basics,
                actionRow,
                preview,
                advancedToggle,
                _advancedSettings,
                _perSubjectHeading,
                _reportRows
            }
        };
        Content = new ScrollView { Content = content };
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
        _classLabel.Text = $"{LocalizationService.T("Class")}: {_context.CurrentClass.Name}";
        UpdateSubjectText();
        _loading = true;
        _events = (await _database.GetEventsForClassAsync(_context.CurrentClass.Id, includeDisabled: true)).ToList();
        _kindPicker.ItemsSource = _events;
        _kindPicker.SelectedIndex = _events.Count > 0 ? 0 : -1;
        _loading = false;
        await LoadProfilesAsync();
        await RunAsync();
    }

    private static Grid SwitchRow(string title, string help, Switch toggle)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        row.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children = { Ui.Heading(title, 15), Ui.Secondary(help) }
        }, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }

    private ClassEventDefinition? SelectedEvent => _kindPicker.SelectedItem as ClassEventDefinition;

    private async Task RunAsync()
    {
        var classGroup = _context.CurrentClass;
        var selectedEvent = SelectedEvent;
        if (classGroup is null || selectedEvent is null)
        {
            return;
        }
        var requestVersion = Interlocked.Increment(ref _reportRequestVersion);
        var start = _startPicker.Date ?? DateTime.Today;
        var end = _endPicker.Date ?? DateTime.Today;
        var completedOnly = _completedOnly.IsToggled;
        var report = await _reports.CreateEventAsync(
            classGroup.Id,
            selectedEvent.Event.Id,
            start,
            end,
            completedOnly);
        if (requestVersion != Volatile.Read(ref _reportRequestVersion))
        {
            return;
        }

        _summaryLabel.Text = $"{selectedEvent.Name} · {FormatSummary(report.Overall, selectedEvent)}";
        var missingDates = report.MissingDates.Count == 0
            ? string.Empty
            : $" · {LocalizationService.T("MissingDates")}: {string.Join(", ", report.MissingDates.Take(5).Select(d => LocalizationService.FormatDate(d)))}";
        _qualityLabel.Text = string.Format(
            LocalizationService.T("DataQualitySummary"),
            report.CalendarDayCount,
            report.CompletedDayCount,
            report.IncompleteDates.Count,
            report.MissingDates.Count,
            missingDates);
        if (report.MissingDates.Count > 0 || report.IncompleteDates.Count > 0)
        {
            _qualityLabel.SetAppThemeColor(
                Label.TextColorProperty,
                Color.FromArgb("#991B1B"),
                Color.FromArgb("#FCA5A5"));
        }
        else
        {
            _qualityLabel.SetAppThemeColor(
                Label.TextColorProperty,
                Color.FromArgb("#166534"),
                Color.FromArgb("#86EFAC"));
        }

        _reportRows.Clear();
        foreach (var row in report.Rows)
        {
            var values = FormatSummary(row.Summary, selectedEvent);
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(12, 8)
            };
            grid.Add(new VerticalStackLayout
            {
                Spacing = 3,
                Children = { Ui.Heading(row.Name, 17), Ui.Secondary(values) }
            }, 0, 0);
            grid.Add(new VerticalStackLayout
            {
                Spacing = 1,
                HorizontalOptions = LayoutOptions.End,
                Children =
                {
                    new Label
                    {
                        Text = row.RateText,
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 17,
                        HorizontalTextAlignment = TextAlignment.End
                    },
                    Ui.Secondary(PositiveRateLabel(selectedEvent))
                }
            }, 1, 0);
            _reportRows.Add(Ui.Card(grid, new Thickness(2)));
        }
    }

    private static string FormatSummary(EventSummary summary, ClassEventDefinition definition) =>
        EventSummaryFormatter.Format(summary, definition.StatusOptions);

    private void UpdateSubjectText()
    {
        var selectedIndex = _orientationPicker.SelectedIndex < 0 ? 0 : _orientationPicker.SelectedIndex;
        var subjectLabel = SubjectLabels.ForClass(_context.CurrentClass);
        _orientationPicker.ItemsSource = new[]
        {
            string.Format(LocalizationService.T("DatesAsRowsForSubject"), subjectLabel),
            string.Format(LocalizationService.T("SubjectsAsRows"), subjectLabel)
        };
        _orientationPicker.SelectedIndex = Math.Min(selectedIndex, 1);
        _perSubjectHeading.Text = string.Format(
            LocalizationService.T("PerSubjectTotals"),
            subjectLabel);
    }

    private static string PositiveRateLabel(ClassEventDefinition definition)
    {
        var labels = definition.StatusOptions
            .Where(option => option.Semantic == EventStatusSemantic.Positive)
            .Select(option => option.Label)
            .ToList();
        var label = labels.Count == 0
            ? LocalizationService.T("PositiveStatus")
            : string.Join("/", labels);
        return string.Format(LocalizationService.T("StatusRate"), label);
    }

    private async void AddBucketClicked(object? sender, EventArgs e)
    {
        var name = _bucketName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync(
                LocalizationService.T("SeparateTotalNeedsNameTitle"),
                LocalizationService.T("SeparateTotalNeedsNameMessage"),
                LocalizationService.T("OK"));
            return;
        }
        if (_bucketWeekday.SelectedIndex <= 0
            && !_bucketUseDate.IsToggled
            && string.IsNullOrWhiteSpace(_bucketLabel.Text))
        {
            await DisplayAlertAsync(
                LocalizationService.T("SeparateTotalNeedsRuleTitle"),
                LocalizationService.T("SeparateTotalNeedsRuleMessage"),
                LocalizationService.T("OK"));
            return;
        }
        var bucket = _buckets.FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (bucket is null)
        {
            bucket = new ExportBucketDefinition { Name = name };
            _buckets.Add(bucket);
        }
        bucket.ExcludeFromBase = _bucketExclude.IsToggled;
        if (_bucketWeekday.SelectedIndex > 0)
        {
            var weekday = _bucketWeekday.SelectedIndex - 1;
            if (!bucket.Weekdays.Contains(weekday))
            {
                bucket.Weekdays.Add(weekday);
            }
        }
        if (_bucketUseDate.IsToggled)
        {
            var dateKey = DatabaseService.DateKey(_bucketDate.Date ?? DateTime.Today);
            if (!bucket.DateKeys.Contains(dateKey))
            {
                bucket.DateKeys.Add(dateKey);
            }
        }
        if (!string.IsNullOrWhiteSpace(_bucketLabel.Text))
        {
            var label = _bucketLabel.Text.Trim();
            if (!bucket.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                bucket.Labels.Add(label);
            }
        }
        if (bucket.Weekdays.Count == 0 && bucket.DateKeys.Count == 0 && bucket.Labels.Count == 0)
        {
            _buckets.Remove(bucket);
            return;
        }
        _bucketName.Text = string.Empty;
        _bucketLabel.Text = string.Empty;
        _bucketUseDate.IsToggled = false;
        _bucketWeekday.SelectedIndex = 0;
        RefreshBucketRows();
    }

    private void RefreshBucketRows()
    {
        _bucketRows.Clear();
        var dayNames = LocalizationService.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        foreach (var bucket in _buckets.ToList())
        {
            var rules = new List<string>();
            rules.AddRange(bucket.Weekdays.Select(day => dayNames[day]));
            rules.AddRange(bucket.DateKeys.Select(dateKey => LocalizationService.FormatDateKey(dateKey)));
            rules.AddRange(bucket.Labels.Select(label => $"#{label}"));
            var remove = Ui.SecondaryButton(
                "×",
                string.Format(LocalizationService.T("RemoveSummaryColumn"), bucket.Name));
            remove.Clicked += (_, _) =>
            {
                _buckets.Remove(bucket);
                RefreshBucketRows();
            };
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            row.Add(Ui.Secondary($"{bucket.Name}: {string.Join(", ", rules)}{(bucket.ExcludeFromBase ? $" · {LocalizationService.T("ExcludedFromRegular")}" : string.Empty)}"), 0, 0);
            row.Add(remove, 1, 0);
            _bucketRows.Add(Ui.Card(row, new Thickness(8, 4)));
        }
    }

    private ExportProfileDefinition BuildDefinition() => new()
    {
        Name = string.IsNullOrWhiteSpace(_profileName.Text)
            ? LocalizationService.T("DefaultProfile")
            : _profileName.Text.Trim(),
        EventDefinitionId = SelectedEvent?.Event.Id ?? 0,
        Kind = SelectedEvent?.Event.SystemKey == "meal" ? TrackingKind.Meal : TrackingKind.Attendance,
        DatesAsRows = _orientationPicker.SelectedIndex != 1,
        BaseBucketName = string.IsNullOrWhiteSpace(_baseBucketName.Text)
            ? LocalizationService.T("Regular")
            : _baseBucketName.Text.Trim(),
        CompletedOnly = _completedOnly.IsToggled,
        IncludeNotes = _includeNotes.IsToggled,
        IncludeRawData = _includeRaw.IsToggled,
        PositiveSymbol = string.IsNullOrWhiteSpace(_positiveSymbol.Text) ? "✓" : _positiveSymbol.Text.Trim(),
        Buckets = _buckets.Select(CloneBucket).ToList()
    };

    private static ExportBucketDefinition CloneBucket(ExportBucketDefinition bucket) => new()
    {
        Name = bucket.Name,
        ExcludeFromBase = bucket.ExcludeFromBase,
        Weekdays = bucket.Weekdays.ToList(),
        DateKeys = bucket.DateKeys.ToList(),
        Labels = bucket.Labels.ToList()
    };

    private async void SaveProfileClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var definition = BuildDefinition();
        await _database.SaveExportProfileAsync(_context.CurrentClass.Id, definition);
        await LoadProfilesAsync();
        _profilePicker.SelectedItem = _profiles.FirstOrDefault(p => p.Name == definition.Name);
    }

    private async void DeleteProfileClicked(object? sender, EventArgs e)
    {
        if (_profilePicker.SelectedItem is not ExportProfile profile)
        {
            return;
        }
        var confirmed = await DisplayAlertAsync(
            LocalizationService.T("DeleteProfileTitle"),
            string.Format(LocalizationService.T("DeleteProfileMessage"), profile.Name),
            LocalizationService.T("Delete"),
            LocalizationService.T("Cancel"));
        if (!confirmed)
        {
            return;
        }
        await _database.DeleteExportProfileAsync(profile);
        await LoadProfilesAsync();
    }

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _reportRequestVersion);
        base.OnDisappearing();
    }

    private async Task LoadProfilesAsync()
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        _loading = true;
        _profiles = (await _database.GetExportProfilesAsync(_context.CurrentClass.Id)).ToList();
        _profilePicker.ItemsSource = _profiles;
        _profilePicker.SelectedIndex = _profiles.Count > 0 ? 0 : -1;
        _loading = false;
    }

    private void LoadSelectedProfile()
    {
        if (_profilePicker.SelectedItem is not ExportProfile profile)
        {
            return;
        }
        var definition = DatabaseService.ParseExportProfile(profile);
        _loading = true;
        _profileName.Text = definition.Name;
        var eventId = definition.EventDefinitionId;
        if (eventId == 0)
        {
            var systemKey = definition.Kind == TrackingKind.Meal ? "meal" : "attendance";
            eventId = _events.FirstOrDefault(e => e.Event.SystemKey == systemKey)?.Event.Id ?? 0;
        }
        _kindPicker.SelectedIndex = _events.FindIndex(e => e.Event.Id == eventId);
        _orientationPicker.SelectedIndex = definition.DatesAsRows ? 0 : 1;
        _baseBucketName.Text = definition.BaseBucketName;
        _completedOnly.IsToggled = definition.CompletedOnly;
        _includeNotes.IsToggled = definition.IncludeNotes;
        _includeRaw.IsToggled = definition.IncludeRawData;
        _positiveSymbol.Text = definition.PositiveSymbol;
        _buckets.Clear();
        _buckets.AddRange(definition.Buckets.Select(CloneBucket));
        RefreshBucketRows();
        _loading = false;
    }

    private async void ExportClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        try
        {
            var path = await _exports.ExportAsync(
                _context.CurrentClass.Id,
                _startPicker.Date ?? DateTime.Today,
                _endPicker.Date ?? DateTime.Today,
                BuildDefinition());
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = LocalizationService.T("ExportExcel"),
                File = new ShareFile(path)
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                LocalizationService.T("ExportFailed"),
                exception.Message,
                LocalizationService.T("OK"));
        }
    }
}

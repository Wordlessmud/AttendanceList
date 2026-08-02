using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class BackfillRosterPage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly MotionService _motion;
    private readonly ClassGroup _classGroup;
    private readonly DateTime _date;
    private readonly string _subjectLabel;
    private readonly Label _context = Ui.Secondary();
    private readonly Label _guidance = Ui.Secondary();
    private readonly VerticalStackLayout _rosterRows = new() { Spacing = 8 };
    private readonly Entry _historicalName = new();
    private readonly Button _useCurrentRoster;
    private readonly Button _confirm;
    private readonly List<CandidateState> _states = [];
    private RosterSource _source = RosterSource.ManualSelection;
    private bool _loaded;
    private bool _updatingSelection;

    public BackfillRosterPage(
        DatabaseService database,
        MotionService motion,
        ClassGroup classGroup,
        DateTime date)
    {
        _database = database;
        _motion = motion;
        _classGroup = classGroup;
        _date = date.Date;
        _subjectLabel = SubjectLabels.ForClass(classGroup);
        Title = LocalizationService.T("BackfillRosterTitle");
        Padding = new Thickness(16);

        _historicalName.Placeholder = string.Format(
            LocalizationService.T("HistoricalOnlySubjectPlaceholder"),
            _subjectLabel);
        _historicalName.ReturnType = ReturnType.Done;
        _historicalName.Completed += AddHistoricalClicked;
        var addHistorical = Ui.SecondaryButton(string.Format(
            LocalizationService.T("AddHistoricalOnlySubject"),
            _subjectLabel));
        addHistorical.Clicked += AddHistoricalClicked;
        var addHistoricalRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        addHistoricalRow.Add(_historicalName, 0, 0);
        addHistoricalRow.Add(addHistorical, 1, 0);

        _useCurrentRoster = Ui.SecondaryButton(LocalizationService.T("UseCurrentRosterAssumption"));
        _useCurrentRoster.Clicked += UseCurrentRosterClicked;
        _confirm = new Button { Text = LocalizationService.T("ConfirmBackfillRoster"), IsEnabled = false };
        _confirm.Clicked += ConfirmClicked;

        var scrollContent = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Ui.Heading(LocalizationService.FormatDate(_date, "D"), 21),
                _context,
                Ui.Secondary(string.Format(LocalizationService.T("BackfillRosterHelpForSubject"), _subjectLabel)),
                Ui.Card(new VerticalStackLayout
                {
                    Spacing = 6,
                    Children = { _guidance, _useCurrentRoster }
                }),
                Ui.Heading(string.Format(LocalizationService.T("ChooseBackfillSubjects"), _subjectLabel), 17),
                _rosterRows,
                Ui.Card(new VerticalStackLayout
                {
                    Spacing = 7,
                    Children =
                    {
                        Ui.Secondary(string.Format(
                            LocalizationService.T("HistoricalOnlySubjectHelp"),
                            _subjectLabel)),
                        addHistoricalRow
                    }
                })
            }
        };
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 12
        };
        root.Add(new ScrollView { Content = scrollContent }, 0, 0);
        root.Add(_confirm, 0, 1);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            await Ui.RunSafelyAsync(this, LoadAsync);
        }
    }

    private async Task LoadAsync()
    {
        var organization = await _database.GetOrganizationAsync(_classGroup.OrganizationId);
        _context.Text = $"{organization.Name} · {_classGroup.Name}";
        var candidates = await _database.GetBackfillRosterCandidatesAsync(_classGroup.Id, _date);
        var hasHistoricalRoster = candidates.Any(value => value.WasMemberOnDate);
        _source = hasHistoricalRoster
            ? RosterSource.HistoricalMembership
            : RosterSource.ManualSelection;
        _guidance.Text = LocalizationService.T(hasHistoricalRoster
            ? "HistoricalRosterFound"
            : "HistoricalRosterUnavailable");
        _useCurrentRoster.IsEnabled = candidates.Any(value => value.IsCurrentMember);

        _updatingSelection = true;
        foreach (var candidate in candidates)
        {
            AddCandidate(candidate, candidate.WasMemberOnDate, candidate.IsExpectedOnDate);
        }
        _updatingSelection = false;
        _loaded = true;
        UpdateConfirmState();
    }

    private void AddCandidate(
        BackfillRosterCandidate? candidate,
        bool selected,
        bool expected,
        string historicalOnlyName = "")
    {
        var include = new CheckBox { IsChecked = selected };
        var expectedCheck = new CheckBox { IsChecked = expected, IsEnabled = selected };
        var name = candidate?.Name ?? historicalOnlyName;
        var statusKey = candidate is null
            ? "HistoricalOnlyPerson"
            : candidate.WasMemberOnDate
                ? "HistoricalMember"
                : candidate.IsCurrentMember
                    ? "CurrentMember"
                    : "ArchivedMember";
        var state = new CandidateState(candidate, historicalOnlyName, include, expectedCheck);
        _states.Add(state);

        include.CheckedChanged += (_, args) =>
        {
            expectedCheck.IsEnabled = args.Value;
            if (!_updatingSelection)
            {
                _source = RosterSource.ManualSelection;
            }
            UpdateConfirmState();
        };
        expectedCheck.CheckedChanged += (_, _) =>
        {
            if (!_updatingSelection)
            {
                _source = RosterSource.ManualSelection;
            }
        };

        var expectedRow = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                expectedCheck,
                Ui.Secondary(LocalizationService.T("ExpectedForBackfillDate"))
            }
        };
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        row.Add(include, 0, 0);
        row.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = name, FontSize = 16 },
                Ui.Secondary(LocalizationService.T(statusKey))
            }
        }, 1, 0);
        row.Add(expectedRow, 2, 0);
        _rosterRows.Add(Ui.Card(row, new Thickness(10, 7)));
    }

    private void UseCurrentRosterClicked(object? sender, EventArgs e)
    {
        _updatingSelection = true;
        foreach (var state in _states)
        {
            var use = state.Candidate?.IsCurrentMember == true;
            state.Include.IsChecked = use;
            state.Expected.IsChecked = use && state.Candidate!.IsExpectedOnDate;
        }
        _updatingSelection = false;
        _source = RosterSource.CurrentRosterAssumption;
        _guidance.Text = LocalizationService.T("CurrentRosterAssumptionSelected");
        UpdateConfirmState();
    }

    private async void AddHistoricalClicked(object? sender, EventArgs e)
    {
        var name = _historicalName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        if (_states.Any(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            await DisplayAlertAsync(
                LocalizationService.T("DuplicateNameTitle"),
                LocalizationService.T("DuplicateBackfillName"),
                LocalizationService.T("OK"));
            return;
        }

        _source = RosterSource.ManualSelection;
        AddCandidate(null, selected: true, expected: true, historicalOnlyName: name);
        _historicalName.Text = string.Empty;
        UpdateConfirmState();
    }

    private async void ConfirmClicked(object? sender, EventArgs e)
    {
        var selections = _states
            .Where(value => value.Include.IsChecked)
            .Select(value => new BackfillRosterSelection
            {
                MembershipId = value.Candidate?.MembershipId ?? 0,
                HistoricalOnlyName = value.HistoricalOnlyName,
                IsExpected = value.Expected.IsChecked
            })
            .ToList();
        if (selections.Count == 0)
        {
            await DisplayAlertAsync(
                LocalizationService.T("BackfillRosterTitle"),
                string.Format(LocalizationService.T("BackfillSelectSubjectRequired"), _subjectLabel),
                LocalizationService.T("OK"));
            return;
        }
        var confirmed = await DisplayAlertAsync(
            LocalizationService.T("ConfirmBackfillRoster"),
            string.Format(
                LocalizationService.T("ConfirmBackfillRosterForSubject"),
                selections.Count,
                LocalizationService.FormatDate(_date),
                _subjectLabel),
            LocalizationService.T("CreateRecord"),
            LocalizationService.T("Cancel"));
        if (!confirmed)
        {
            return;
        }

        _confirm.IsEnabled = false;
        try
        {
            var day = await _database.SaveBackfillRosterAsync(
                _classGroup.Id,
                _date,
                selections,
                _source);
            var detail = new DayDetailPage(_database, _motion, day);
            await Navigation.PushAsync(detail);
            Navigation.RemovePage(this);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                LocalizationService.T("Error"),
                exception.Message,
                LocalizationService.T("OK"));
            UpdateConfirmState();
        }
    }

    private void UpdateConfirmState() =>
        _confirm.IsEnabled = _states.Any(value => value.Include.IsChecked);

    private sealed record CandidateState(
        BackfillRosterCandidate? Candidate,
        string HistoricalOnlyName,
        CheckBox Include,
        CheckBox Expected)
    {
        public string Name => Candidate?.Name ?? HistoricalOnlyName;
    }
}

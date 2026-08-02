using System.Collections.ObjectModel;
using AttendanceList.Helpers;
using AttendanceList.Models;
using AttendanceList.Services;

namespace AttendanceList.Pages;

public sealed class PeoplePage : ContentPage
{
    private readonly DatabaseService _database;
    private readonly ClassContextService _context;
    private readonly ReminderCoordinator _reminders;
    private readonly MotionService _motion;
    private readonly ObservableCollection<ClassMember> _people = [];
    private readonly Picker _organizationPicker = new();
    private readonly Picker _classPicker = new();
    private readonly Entry _nameEntry = new();
    private readonly Switch _showArchivedSwitch = new();
    private readonly Label _showArchivedLabel = Ui.Secondary();
    private readonly Label _emptyPeopleLabel = Ui.Secondary();
    private readonly Label _addFeedback = Ui.Secondary();
    private readonly CollectionView _collection;
    private readonly Grid _classActions = new();
    private readonly Button _classScheduleButton;
    private readonly Button _calendarButton;
    private readonly Button _eventsButton;
    private readonly Button _subjectLabelButton;
    private readonly Button _archiveClassButton;
    private readonly Button _restoreClassButton;
    private readonly Button _addButton;
    private bool _loadingSelectors;
    private bool _adding;
    private int? _recentlyAddedMembershipId;
    private bool? _lastSingleColumnActionLayout;
    private bool? _lastRestoreButtonVisibility;

    public PeoplePage(
        DatabaseService database,
        ClassContextService context,
        ReminderCoordinator reminders,
        MotionService motion)
    {
        _database = database;
        _context = context;
        _reminders = reminders;
        _motion = motion;
        Title = LocalizationService.T("ClassesAndPeople");
        Padding = new Thickness(16);

        _organizationPicker.Title = LocalizationService.T("Organization");
        _organizationPicker.ItemDisplayBinding = new Binding(nameof(Organization.Name));
        _organizationPicker.SelectedIndexChanged += OrganizationChanged;
        _classPicker.Title = LocalizationService.T("Class");
        _classPicker.ItemDisplayBinding = new Binding(nameof(ClassGroup.Name));
        _classPicker.SelectedIndexChanged += ClassChanged;

        var addOrganization = Ui.SecondaryButton("+", LocalizationService.T("AddOrganization"));
        addOrganization.SetDynamicResource(MinimumWidthRequestProperty, "ButtonMinimumHeight");
        addOrganization.Padding = 0;
        addOrganization.Clicked += AddOrganizationClicked;
        var renameOrganization = Ui.SecondaryButton(LocalizationService.T("Rename"));
        renameOrganization.Clicked += RenameOrganizationClicked;
        var organizationRow = SelectorRow(_organizationPicker, addOrganization, renameOrganization);

        var addClass = Ui.SecondaryButton("+", LocalizationService.T("AddClass"));
        addClass.SetDynamicResource(MinimumWidthRequestProperty, "ButtonMinimumHeight");
        addClass.Padding = 0;
        addClass.Clicked += AddClassClicked;
        var renameClass = Ui.SecondaryButton(LocalizationService.T("Rename"));
        renameClass.Clicked += RenameClassClicked;
        var classRow = SelectorRow(_classPicker, addClass, renameClass);

        _classScheduleButton = Ui.SecondaryButton(LocalizationService.T("OperatingDays"));
        _classScheduleButton.Clicked += ClassScheduleClicked;
        _calendarButton = Ui.SecondaryButton(LocalizationService.T("CalendarOverrides"));
        _calendarButton.Clicked += CalendarClicked;
        _eventsButton = Ui.SecondaryButton(LocalizationService.T("Events"));
        _eventsButton.Clicked += EventsClicked;
        _subjectLabelButton = Ui.SecondaryButton(LocalizationService.T("SubjectLabelAction"));
        _subjectLabelButton.Clicked += SubjectLabelClicked;
        _archiveClassButton = Ui.DestructiveSecondaryButton(LocalizationService.T("ArchiveClass"));
        _archiveClassButton.Clicked += ArchiveClassClicked;
        _restoreClassButton = Ui.SecondaryButton(LocalizationService.T("RestoreClass"));
        _restoreClassButton.Clicked += RestoreClassClicked;
        _restoreClassButton.IsVisible = false;

        _classActions.ColumnSpacing = 8;
        _classActions.RowSpacing = 8;
        _classActions.SizeChanged += (_, _) => ApplyClassActionLayout();
        _classActions.Children.Add(_classScheduleButton);
        _classActions.Children.Add(_calendarButton);
        _classActions.Children.Add(_eventsButton);
        _classActions.Children.Add(_subjectLabelButton);
        _classActions.Children.Add(_restoreClassButton);
        _classActions.Children.Add(_archiveClassButton);
        ApplyClassActionLayout(force: true);

        _nameEntry.ReturnType = ReturnType.Done;
        _nameEntry.Completed += AddClicked;
        _addButton = new Button { Text = LocalizationService.T("Add") };
        _addButton.Clicked += AddClicked;
        var addRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        addRow.Add(_nameEntry, 0, 0);
        addRow.Add(_addButton, 1, 0);

        _addFeedback.IsVisible = false;
        _addFeedback.SetAppThemeColor(
            Label.TextColorProperty,
            Color.FromArgb("#166534"),
            Color.FromArgb("#86EFAC"));
        var addContent = new VerticalStackLayout
        {
            Spacing = 5,
            Children = { addRow, _addFeedback }
        };

        var archivedRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _showArchivedSwitch, _showArchivedLabel }
        };
        _showArchivedSwitch.Toggled += async (_, _) => await LoadMembersAsync();

        _collection = new CollectionView
        {
            ItemsSource = _people,
            SelectionMode = SelectionMode.None,
            EmptyView = _emptyPeopleLabel,
            ItemTemplate = CreateTemplate()
        };

        var contextCard = Ui.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Ui.Secondary(LocalizationService.T("Organization")),
                organizationRow,
                Ui.Secondary(LocalizationService.T("Class")),
                classRow,
                _classActions
            }
        });
        var scroll = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 12
        };
        scroll.Add(contextCard, 0, 0);
        scroll.Add(Ui.Card(addContent), 0, 1);
        scroll.Add(archivedRow, 0, 2);
        scroll.Add(_collection, 0, 3);
        Content = scroll;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Ui.RunSafelyAsync(this, LoadSelectorsAsync);
    }

    private static Grid SelectorRow(Picker picker, params Button[] buttons)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Add(picker, 0, 0);
        for (var index = 0; index < buttons.Length; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Add(buttons[index], index + 1, 0);
        }
        return row;
    }

    private void ApplyClassActionLayout(bool force = false)
    {
        // The card is wide enough for two columns on ordinary phones. At very
        // narrow widths (or large accessibility scaling), a single column keeps
        // labels and touch targets from crowding each other.
        var singleColumn = _classActions.Width > 0 && _classActions.Width < 300;
        var restoreVisible = _restoreClassButton.IsVisible;
        if (!force &&
            _lastSingleColumnActionLayout == singleColumn &&
            _lastRestoreButtonVisibility == restoreVisible)
        {
            return;
        }

        _lastSingleColumnActionLayout = singleColumn;
        _lastRestoreButtonVisibility = restoreVisible;
        _classActions.ColumnDefinitions.Clear();
        _classActions.RowDefinitions.Clear();

        if (singleColumn)
        {
            _classActions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var rowCount = restoreVisible ? 6 : 5;
            for (var index = 0; index < rowCount; index++)
            {
                _classActions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            PositionAction(_classScheduleButton, 0, 0);
            PositionAction(_calendarButton, 0, 1);
            PositionAction(_eventsButton, 0, 2);
            PositionAction(_subjectLabelButton, 0, 3);
            PositionAction(_restoreClassButton, 0, 4);
            PositionAction(_archiveClassButton, 0, restoreVisible ? 5 : 4);
            return;
        }

        _classActions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _classActions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var twoColumnRows = restoreVisible ? 4 : 3;
        for (var index = 0; index < twoColumnRows; index++)
        {
            _classActions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        PositionAction(_classScheduleButton, 0, 0);
        PositionAction(_calendarButton, 1, 0);
        PositionAction(_eventsButton, 0, 1);
        PositionAction(_subjectLabelButton, 1, 1);
        PositionAction(_restoreClassButton, 0, 2, 2);
        PositionAction(_archiveClassButton, 0, restoreVisible ? 3 : 2, 2);
    }

    private static void PositionAction(Button button, int column, int row, int columnSpan = 1)
    {
        Grid.SetColumn(button, column);
        Grid.SetRow(button, row);
        Grid.SetColumnSpan(button, columnSpan);
        button.HorizontalOptions = LayoutOptions.Fill;
    }

    private async Task LoadSelectorsAsync()
    {
        await _context.EnsureInitializedAsync();
        _loadingSelectors = true;
        var organizations = (await _database.GetOrganizationsAsync()).ToList();
        _organizationPicker.ItemsSource = organizations;
        _organizationPicker.SelectedIndex = organizations.FindIndex(o => o.Id == _context.CurrentOrganization?.Id);
        if (_context.CurrentOrganization is not null)
        {
            var allClasses = await _database.GetClassesAsync(
                _context.CurrentOrganization.Id,
                includeArchived: true);
            var classes = allClasses.Where(classGroup => !classGroup.IsArchived).ToList();
            _classPicker.ItemsSource = classes;
            _classPicker.SelectedIndex = classes.FindIndex(c => c.Id == _context.CurrentClass?.Id);
            _restoreClassButton.IsVisible = allClasses.Any(classGroup => classGroup.IsArchived);
            ApplyClassActionLayout();
        }
        _loadingSelectors = false;
        UpdateSubjectText();
        await LoadMembersAsync();
    }

    private async void OrganizationChanged(object? sender, EventArgs e)
    {
        if (_loadingSelectors || _organizationPicker.SelectedItem is not Organization organization)
        {
            return;
        }
        await _context.SetOrganizationAsync(organization);
        await LoadSelectorsAsync();
    }

    private async void ClassChanged(object? sender, EventArgs e)
    {
        if (_loadingSelectors || _classPicker.SelectedItem is not ClassGroup classGroup)
        {
            return;
        }
        _context.SetClass(classGroup);
        UpdateSubjectText();
        await LoadMembersAsync();
    }

    private DataTemplate CreateTemplate() => new(() =>
    {
        var name = new Label { FontSize = 17, VerticalTextAlignment = TextAlignment.Center };
        name.SetBinding(Label.TextProperty, nameof(ClassMember.DisplayName));

        var edit = Ui.SecondaryButton(LocalizationService.T("Edit"));
        edit.Clicked += async (_, _) =>
        {
            if (edit.BindingContext is not ClassMember member)
            {
                return;
            }
            var updatedName = await DisplayPromptAsync(
                LocalizationService.T("EditName"),
                LocalizationService.T("EnterNewName"),
                initialValue: member.DisplayName);
            if (!string.IsNullOrWhiteSpace(updatedName))
            {
                await _database.UpdateMemberNameAsync(member, updatedName);
                await LoadMembersAsync();
            }
        };

        var schedule = Ui.SecondaryButton(LocalizationService.T("Schedule"));
        schedule.Clicked += async (_, _) =>
        {
            if (schedule.BindingContext is ClassMember member)
            {
                await Navigation.PushAsync(new WeekdayMaskPage(
                    member.DisplayName,
                    member.Membership.ExpectedDaysMask,
                    async mask => await _database.UpdateMembershipScheduleAsync(member.Membership, mask)));
            }
        };

        Border? card = null;
        var action = Ui.SecondaryButton(string.Empty);
        action.BindingContextChanged += (_, _) =>
        {
            if (action.BindingContext is ClassMember member)
            {
                action.Text = member.IsActive
                    ? LocalizationService.T("Archive")
                    : LocalizationService.T("Restore");
                SemanticProperties.SetDescription(action, $"{action.Text}: {member.DisplayName}");
            }
        };
        action.Clicked += async (_, _) =>
        {
            if (action.BindingContext is ClassMember member)
            {
                if (member.IsActive)
                {
                    var subjectLabel = SubjectLabels.ForClass(_context.CurrentClass);
                    var confirmed = await DisplayAlertAsync(
                        string.Format(LocalizationService.T("ArchiveSubjectTitle"), subjectLabel),
                        string.Format(
                            LocalizationService.T("ArchiveSubjectMessage"),
                            subjectLabel,
                            member.DisplayName),
                        LocalizationService.T("Archive"),
                        LocalizationService.T("Cancel"));
                    if (!confirmed)
                    {
                        return;
                    }
                }
                await _database.SetMembershipActiveAsync(member.Membership, !member.IsActive);
                if (!member.IsActive && !_showArchivedSwitch.IsToggled)
                {
                    if (card is not null)
                    {
                        await _motion.RemoveAsync(card);
                    }
                    _people.Remove(member);
                }
                else
                {
                    await LoadMembersAsync();
                }
            }
        };

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(12, 8),
            ColumnSpacing = 6
        };
        row.Add(name, 0, 0);
        row.Add(edit, 1, 0);
        row.Add(schedule, 2, 0);
        row.Add(action, 3, 0);
        card = Ui.Card(row, new Thickness(4));
        card.BindingContextChanged += (_, _) =>
        {
            card.Opacity = 1;
            card.Scale = 1;
            card.TranslationX = 0;
            if (card.BindingContext is ClassMember member
                && member.Membership.Id == _recentlyAddedMembershipId)
            {
                _recentlyAddedMembershipId = null;
                Dispatcher.Dispatch(async () => await _motion.EnterStepAsync(card, 1));
            }
        };
        return card;
    });

    private async void AddOrganizationClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(LocalizationService.T("AddOrganization"), LocalizationService.T("Name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var organization = await _database.AddOrganizationAsync(name);
        await _context.SetOrganizationAsync(organization);
        await LoadSelectorsAsync();
    }

    private async void RenameOrganizationClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentOrganization is null)
        {
            return;
        }
        var name = await DisplayPromptAsync(
            LocalizationService.T("RenameOrganization"),
            LocalizationService.T("Name"),
            initialValue: _context.CurrentOrganization.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _database.UpdateOrganizationNameAsync(_context.CurrentOrganization, name);
            await LoadSelectorsAsync();
        }
    }

    private async void AddClassClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentOrganization is null)
        {
            return;
        }
        var name = await DisplayPromptAsync(LocalizationService.T("AddClass"), LocalizationService.T("Name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var classGroup = await _database.AddClassAsync(_context.CurrentOrganization.Id, name);
        _context.SetClass(classGroup);
        await LoadSelectorsAsync();
    }

    private async void RenameClassClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var name = await DisplayPromptAsync(
            LocalizationService.T("RenameClass"),
            LocalizationService.T("Name"),
            initialValue: _context.CurrentClass.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _context.CurrentClass.Name = name;
            await _database.UpdateClassAsync(_context.CurrentClass);
            await LoadSelectorsAsync();
        }
    }

    private async void ArchiveClassClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null || _context.CurrentOrganization is null)
        {
            return;
        }
        var classes = await _database.GetClassesAsync(_context.CurrentOrganization.Id);
        if (classes.Count <= 1)
        {
            await DisplayAlertAsync(
                LocalizationService.T("CannotArchiveClassTitle"),
                LocalizationService.T("CannotArchiveOnlyClass"),
                LocalizationService.T("OK"));
            return;
        }
        var archive = await DisplayAlertAsync(
            LocalizationService.T("ArchiveClass"),
            LocalizationService.T("ArchiveClassMessage"),
            LocalizationService.T("Archive"),
            LocalizationService.T("Cancel"));
        if (!archive)
        {
            return;
        }
        await _database.SetClassArchivedAsync(_context.CurrentClass, true);
        await _reminders.RescheduleAsync(requestPermission: false);
        await _context.SetOrganizationAsync(_context.CurrentOrganization);
        await LoadSelectorsAsync();
    }

    private async void ClassScheduleClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var classGroup = _context.CurrentClass;
        await Navigation.PushAsync(new WeekdayMaskPage(
            LocalizationService.T("OperatingDays"),
            classGroup.OperatingDaysMask,
            async mask =>
            {
                classGroup.OperatingDaysMask = mask;
                await _database.UpdateClassAsync(classGroup);
            }));
    }

    private async void RestoreClassClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentOrganization is null)
        {
            return;
        }
        var archived = (await _database.GetClassesAsync(_context.CurrentOrganization.Id, includeArchived: true))
            .Where(classGroup => classGroup.IsArchived)
            .ToList();
        if (archived.Count == 0)
        {
            await DisplayAlertAsync(
                LocalizationService.T("RestoreClass"),
                LocalizationService.T("NoArchivedClasses"),
                LocalizationService.T("OK"));
            return;
        }
        var selected = await DisplayActionSheetAsync(
            LocalizationService.T("RestoreClass"),
            LocalizationService.T("Cancel"),
            null,
            archived.Select(classGroup => classGroup.Name).ToArray());
        var classToRestore = archived.FirstOrDefault(classGroup => classGroup.Name == selected);
        if (classToRestore is null)
        {
            return;
        }
        await _database.SetClassArchivedAsync(classToRestore, false);
        await _reminders.RescheduleAsync(requestPermission: false);
        _context.SetClass(classToRestore);
        await LoadSelectorsAsync();
    }

    private async void CalendarClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is not null)
        {
            await Navigation.PushAsync(new ClassCalendarPage(_database, _context.CurrentClass));
        }
    }

    private async void EventsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new EventsPage(_database, _context, _reminders));
    }

    private async void SubjectLabelClicked(object? sender, EventArgs e)
    {
        if (_context.CurrentClass is null)
        {
            return;
        }
        var classGroup = _context.CurrentClass;
        var currentLabel = SubjectLabels.ForClass(classGroup);
        var label = await DisplayPromptAsync(
            LocalizationService.T("SubjectLabelTitle"),
            string.Format(LocalizationService.T("SubjectLabelPrompt"), currentLabel),
            initialValue: classGroup.SubjectLabel,
            maxLength: 80,
            keyboard: Keyboard.Text);
        if (label is null)
        {
            return;
        }
        classGroup.SubjectLabel = label.Trim();
        await _database.UpdateClassAsync(classGroup);
        UpdateSubjectText();
        await _motion.EmphasizeStateAsync(_subjectLabelButton);
    }

    private async void AddClicked(object? sender, EventArgs e)
    {
        if (_adding || _context.CurrentClass is null)
        {
            return;
        }
        var name = _nameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _nameEntry.Focus();
            return;
        }
        _adding = true;
        _nameEntry.IsEnabled = false;
        _addButton.IsEnabled = false;
        try
        {
            var added = await _database.AddPersonToClassAsync(_context.CurrentClass.Id, name);
            _recentlyAddedMembershipId = added.Membership.Id;
            _nameEntry.Text = string.Empty;
            await LoadMembersAsync();
            var displayed = _people.FirstOrDefault(value => value.Membership.Id == added.Membership.Id);
            if (displayed is not null)
            {
                _collection.ScrollTo(displayed, position: ScrollToPosition.Center, animate: true);
            }
            _addFeedback.Text = string.Format(
                LocalizationService.T("SubjectAdded"),
                SubjectLabels.ForClass(_context.CurrentClass),
                added.DisplayName);
            _addFeedback.IsVisible = true;
            await _motion.EmphasizeStateAsync(_addFeedback);
        }
        finally
        {
            _adding = false;
            _nameEntry.IsEnabled = true;
            _addButton.IsEnabled = true;
            _nameEntry.Focus();
        }
    }

    private async Task LoadMembersAsync()
    {
        _people.Clear();
        if (_context.CurrentClass is null)
        {
            return;
        }
        var members = await _database.GetClassMembersAsync(_context.CurrentClass.Id, _showArchivedSwitch.IsToggled);
        foreach (var member in members)
        {
            _people.Add(member);
        }
    }

    private void UpdateSubjectText()
    {
        var subjectLabel = SubjectLabels.ForClass(_context.CurrentClass);
        _nameEntry.Placeholder = string.Format(LocalizationService.T("NewSubjectName"), subjectLabel);
        _showArchivedLabel.Text = string.Format(LocalizationService.T("ShowArchivedSubjects"), subjectLabel);
        _emptyPeopleLabel.Text = string.Format(LocalizationService.T("NoSubjectsYet"), subjectLabel);
        _addFeedback.IsVisible = false;
        SemanticProperties.SetDescription(
            _nameEntry,
            string.Format(LocalizationService.T("NewSubjectName"), subjectLabel));
    }
}

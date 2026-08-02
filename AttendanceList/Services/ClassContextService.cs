using AttendanceList.Models;

namespace AttendanceList.Services;

public sealed class ClassContextService(DatabaseService database)
{
    private const string OrganizationPreference = "current_organization_id";
    private const string ClassPreference = "current_class_id";
    private readonly SemaphoreSlim _initialiseLock = new(1, 1);
    private bool _initialised;

    public Organization? CurrentOrganization { get; private set; }
    public ClassGroup? CurrentClass { get; private set; }
    public event EventHandler? Changed;

    public async Task EnsureInitializedAsync()
    {
        if (_initialised)
        {
            return;
        }

        await _initialiseLock.WaitAsync();
        try
        {
            if (_initialised)
            {
                return;
            }

            var organizations = await database.GetOrganizationsAsync();
            var savedOrganizationId = Preferences.Default.Get(OrganizationPreference, 0);
            CurrentOrganization = organizations.FirstOrDefault(o => o.Id == savedOrganizationId)
                ?? organizations.FirstOrDefault();

            if (CurrentOrganization is null)
            {
                var context = await database.GetDefaultContextAsync();
                CurrentOrganization = context.Organization;
                CurrentClass = context.ClassGroup;
            }
            else
            {
                var classes = await database.GetClassesAsync(CurrentOrganization.Id);
                var savedClassId = Preferences.Default.Get(ClassPreference, 0);
                CurrentClass = classes.FirstOrDefault(c => c.Id == savedClassId)
                    ?? classes.FirstOrDefault();
                if (CurrentClass is null)
                {
                    CurrentClass = await database.AddClassAsync(
                        CurrentOrganization.Id,
                        LocalizationService.T("DefaultClass"));
                }
            }

            SavePreferences();
            _initialised = true;
        }
        finally
        {
            _initialiseLock.Release();
        }
    }

    public async Task SetOrganizationAsync(Organization organization)
    {
        CurrentOrganization = organization;
        var classes = await database.GetClassesAsync(organization.Id);
        CurrentClass = classes.FirstOrDefault()
            ?? await database.AddClassAsync(organization.Id, LocalizationService.T("DefaultClass"));
        SavePreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetClass(ClassGroup classGroup)
    {
        CurrentClass = classGroup;
        SavePreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshAsync()
    {
        _initialised = false;
        await EnsureInitializedAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SavePreferences()
    {
        if (CurrentOrganization is not null)
        {
            Preferences.Default.Set(OrganizationPreference, CurrentOrganization.Id);
        }
        if (CurrentClass is not null)
        {
            Preferences.Default.Set(ClassPreference, CurrentClass.Id);
        }
    }
}

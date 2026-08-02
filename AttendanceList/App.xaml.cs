using AttendanceList.Pages;
using AttendanceList.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceList;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ReminderCoordinator _reminders;
    private int _restartPending;

    public App(IServiceProvider services, ReminderCoordinator reminders)
    {
        InitializeComponent();
        _services = services;
        _reminders = reminders;
        LocalizationService.ApplySavedCulture();
        ApplyButtonSize();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(CreateRootPage())
        {
            Title = LocalizationService.T("AppTitle"),
            Width = 820,
            Height = 760,
            MinimumWidth = 380,
            MinimumHeight = 560
        };
        _ = _reminders.RescheduleAsync(requestPermission: false);
        return window;
    }

    public async Task RestartUiAsync()
    {
        if (Interlocked.Exchange(ref _restartPending, 1) != 0)
        {
            return;
        }
        try
        {
            // Android pickers may still be dispatching SelectionChanged. Replacing the
            // native page tree after that callback unwinds avoids disposing live controls.
            await Task.Delay(50);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var window in Windows.ToList())
                {
                    window.Page = CreateRootPage();
                    window.Title = LocalizationService.T("AppTitle");
                }
            });
        }
        finally
        {
            Volatile.Write(ref _restartPending, 0);
        }
    }

    public void ApplyButtonSize()
    {
        var size = Preferences.Default.Get("button_size", "medium");
        Resources["ButtonPadding"] = size switch
        {
            "small" => new Thickness(10, 6),
            "large" => new Thickness(18, 14),
            _ => new Thickness(14, 10)
        };
        Resources["ButtonMinimumHeight"] = size switch
        {
            "small" => 44d,
            "large" => 56d,
            _ => 48d
        };
    }

    private Page CreateRootPage() => OnboardingService.IsCompleted
        ? _services.GetRequiredService<AppRootPage>()
        : _services.GetRequiredService<OnboardingPage>();
}

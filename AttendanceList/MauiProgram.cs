using AttendanceList.Pages;
using AttendanceList.Services;
using Microsoft.Extensions.Logging;

namespace AttendanceList;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<ClassContextService>();
        builder.Services.AddSingleton<ReportService>();
        builder.Services.AddSingleton<ExportService>();
        builder.Services.AddSingleton<ReminderCoordinator>();
        builder.Services.AddSingleton<MotionService>();
#if ANDROID
        builder.Services.AddSingleton<ILocalNotificationScheduler, Platforms.Android.AndroidLocalNotificationScheduler>();
#elif WINDOWS
        builder.Services.AddSingleton<ILocalNotificationScheduler, Platforms.Windows.WindowsLocalNotificationScheduler>();
#endif

        builder.Services.AddTransient<AppRootPage>();
        builder.Services.AddTransient<TodayPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<ReportsPage>();
        builder.Services.AddTransient<PeoplePage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<EventsPage>();

        return builder.Build();
    }
}

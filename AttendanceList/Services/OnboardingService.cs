namespace AttendanceList.Services;

public static class OnboardingService
{
    public const string CompletionKey = "onboarding_completed_v3";

    public static bool IsCompleted
    {
        get
        {
            if (Preferences.Default.ContainsKey(CompletionKey))
            {
                return Preferences.Default.Get(CompletionKey, false);
            }
            // Upgrades from pre-onboarding versions already have a database. Do not
            // interrupt those users; the guide remains available from Settings.
            var existingDatabase = File.Exists(Path.Combine(FileSystem.AppDataDirectory, "attendance.db3"));
            if (existingDatabase)
            {
                Preferences.Default.Set(CompletionKey, true);
            }
            else
            {
                // Persist the first-install decision before background services create
                // the database, so an interrupted guide resumes on the next launch.
                Preferences.Default.Set(CompletionKey, false);
            }
            return existingDatabase;
        }
    }

    public static void Complete() => Preferences.Default.Set(CompletionKey, true);

    public static void Reset() => Preferences.Default.Set(CompletionKey, false);
}

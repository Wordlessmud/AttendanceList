using System.Globalization;
using System.Resources;
using Microsoft.Maui.Storage;

namespace AttendanceList.Services;

public static class LocalizationService
{
    private const string PreferenceKey = "language";
    private static readonly ResourceManager ResourceManager =
        new("AttendanceList.Resources.Strings.AppResources", typeof(LocalizationService).Assembly);
    internal static Func<string, string>? TranslationOverride { get; set; }
    internal static CultureInfo? CultureOverride { get; set; }

    public static string CurrentLanguage => NormalizeCultureName(CultureOverride?.Name
        ?? Preferences.Default.Get(PreferenceKey, DeviceLanguage()));

    public static CultureInfo CurrentCulture => CultureOverride
        ?? CultureInfo.GetCultureInfo(CurrentLanguage);

    public static string DatePickerFormat => CurrentLanguage == "zh-CN"
        ? "yyyy年M月d日"
        : "M/d/yyyy";

    public static string FormatDate(DateTime value, string format = "d") =>
        FormatDate(value, format, CurrentCulture);

    internal static string FormatDate(DateTime value, string format, CultureInfo culture) =>
        value.ToString(format, culture);

    public static string FormatDateKey(string dateKey, string format = "d") =>
        DateTime.TryParseExact(
            dateKey,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
                ? FormatDate(date, format)
                : dateKey;

    public static string T(string key)
    {
        if (TranslationOverride is not null)
        {
            return TranslationOverride(key);
        }
        // Android may restore the UI thread's previous culture after an async picker
        // callback. The persisted selection is the authoritative source for UI text.
        return ResourceManager.GetString(key, CurrentCulture) ?? key;
    }

    public static string T(string key, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(NormalizeCultureName(cultureName));
        return ResourceManager.GetString(key, culture) ?? key;
    }

    public static bool IsKnownTranslation(string key, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && new[] { "en", "zh-CN" }.Any(culture =>
            string.Equals(T(key, culture), value, StringComparison.Ordinal));

    public static void ApplySavedCulture()
    {
        SetCultureInternal(CurrentLanguage, persist: false);
    }

    public static void SetCulture(string cultureName)
    {
        SetCultureInternal(cultureName, persist: true);
    }

    private static void SetCultureInternal(string cultureName, bool persist)
    {
        var supported = NormalizeCultureName(cultureName);
        var culture = CultureInfo.GetCultureInfo(supported);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (persist)
        {
            Preferences.Default.Set(PreferenceKey, supported);
        }
    }

    private static string DeviceLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en";

    private static string NormalizeCultureName(string cultureName) =>
        cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en";
}

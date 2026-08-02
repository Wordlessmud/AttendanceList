using System.Globalization;
using System.Resources;
using Microsoft.Maui.Storage;

namespace AttendanceList.Services;

public static class LocalizationService
{
    public const string SystemLanguageMode = "system";
    public const string EnglishLanguage = "en";
    public const string SimplifiedChineseLanguage = "zh-CN";

    public const string SystemDateFormatMode = "system";
    public const string DayMonthYearDateFormatMode = "dmy";
    public const string MonthDayYearDateFormatMode = "mdy";
    public const string YearMonthDayDateFormatMode = "ymd";

    private const string PreferenceKey = "language";
    private const string DateFormatPreferenceKey = "date_format";
    private static readonly CultureInfo SystemUiCulture = CaptureCulture(
        CultureInfo.CurrentUICulture,
        EnglishLanguage);
    private static readonly CultureInfo SystemFormattingCulture = CaptureCulture(
        CultureInfo.CurrentCulture,
        "en-US");
    private static readonly ResourceManager ResourceManager =
        new("AttendanceList.Resources.Strings.AppResources", typeof(LocalizationService).Assembly);

    internal static Func<string, string>? TranslationOverride { get; set; }
    internal static CultureInfo? CultureOverride { get; set; }

    public static string CurrentLanguageMode => CultureOverride is not null
        ? NormalizeCultureName(CultureOverride.Name)
        : NormalizeLanguageMode(Preferences.Default.Get(PreferenceKey, SystemLanguageMode));

    public static string CurrentLanguage => CultureOverride is not null
        ? NormalizeCultureName(CultureOverride.Name)
        : ResolveLanguage(CurrentLanguageMode, SystemUiCulture.Name);

    public static CultureInfo CurrentUiCulture => CultureOverride
        ?? CultureInfo.GetCultureInfo(CurrentLanguage);

    public static CultureInfo CurrentCulture => CultureOverride
        ?? CultureInfo.GetCultureInfo(ResolveFormattingCultureName(
            CurrentLanguage,
            SystemFormattingCulture.Name));

    public static string CurrentDateFormatMode => NormalizeDateFormatMode(
        Preferences.Default.Get(DateFormatPreferenceKey, SystemDateFormatMode));

    public static string DatePickerFormat => ResolveDateFormatPattern(
        CurrentDateFormatMode,
        SystemFormattingCulture);

    public static string FormatDate(DateTime value, string format = "d")
    {
        if (string.Equals(format, "d", StringComparison.Ordinal))
        {
            var culture = CurrentDateFormatMode == SystemDateFormatMode
                ? SystemFormattingCulture
                : CurrentCulture;
            return value.ToString(DatePickerFormat, culture);
        }

        return FormatDate(value, format, CurrentCulture);
    }

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

        // The persisted language mode is authoritative. Android can restore a thread's
        // previous culture after an asynchronous picker callback, so resource lookup must
        // not depend on whichever culture happens to be on that callback thread.
        return ResourceManager.GetString(key, CurrentUiCulture) ?? key;
    }

    public static string T(string key, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(NormalizeCultureName(cultureName));
        return ResourceManager.GetString(key, culture) ?? key;
    }

    public static bool IsKnownTranslation(string key, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && new[] { EnglishLanguage, SimplifiedChineseLanguage }.Any(culture =>
            string.Equals(T(key, culture), value, StringComparison.Ordinal));

    public static void ApplySavedCulture()
    {
        SetLanguageModeInternal(CurrentLanguageMode, persist: false);
    }

    public static void SetLanguageMode(string languageMode)
    {
        SetLanguageModeInternal(languageMode, persist: true);
    }

    public static void SetDateFormatMode(string dateFormatMode)
    {
        Preferences.Default.Set(
            DateFormatPreferenceKey,
            NormalizeDateFormatMode(dateFormatMode));
    }

    // Retained for callers and older code that explicitly selected a language.
    public static void SetCulture(string cultureName)
    {
        SetLanguageMode(cultureName);
    }

    private static void SetLanguageModeInternal(string languageMode, bool persist)
    {
        var normalizedMode = NormalizeLanguageMode(languageMode);
        var language = ResolveLanguage(normalizedMode, SystemUiCulture.Name);
        var uiCulture = CultureInfo.GetCultureInfo(language);
        var formattingCulture = CultureInfo.GetCultureInfo(
            ResolveFormattingCultureName(language, SystemFormattingCulture.Name));

        CultureInfo.DefaultThreadCurrentCulture = formattingCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        CultureInfo.CurrentCulture = formattingCulture;
        CultureInfo.CurrentUICulture = uiCulture;

        if (persist)
        {
            Preferences.Default.Set(PreferenceKey, normalizedMode);
        }
    }

    internal static string DetectSupportedLanguage(string cultureName) =>
        IsSimplifiedChinese(cultureName)
            ? SimplifiedChineseLanguage
            : EnglishLanguage;

    internal static string ResolveLanguage(string languageMode, string systemUiCultureName)
    {
        var normalizedMode = NormalizeLanguageMode(languageMode);
        return normalizedMode == SystemLanguageMode
            ? DetectSupportedLanguage(systemUiCultureName)
            : normalizedMode;
    }

    internal static string ResolveDateFormatPattern(
        string dateFormatMode,
        CultureInfo systemCulture)
    {
        return NormalizeDateFormatMode(dateFormatMode) switch
        {
            DayMonthYearDateFormatMode => "dd/MM/yyyy",
            MonthDayYearDateFormatMode => "MM/dd/yyyy",
            YearMonthDayDateFormatMode => "yyyy-MM-dd",
            _ => systemCulture.DateTimeFormat.ShortDatePattern
        };
    }

    internal static string ResolveFormattingCultureName(
        string language,
        string systemCultureName)
    {
        if (NormalizeCultureName(language) == SimplifiedChineseLanguage)
        {
            return SimplifiedChineseLanguage;
        }

        try
        {
            var systemCulture = CultureInfo.GetCultureInfo(systemCultureName);
            if (!systemCulture.IsNeutralCulture
                && systemCulture.TwoLetterISOLanguageName.Equals(
                    EnglishLanguage,
                    StringComparison.OrdinalIgnoreCase))
            {
                return systemCulture.Name;
            }
        }
        catch (CultureNotFoundException)
        {
            // Fall through to a stable English formatting culture.
        }

        return "en-US";
    }

    private static bool IsSimplifiedChinese(string cultureName) =>
        cultureName.Equals("zh", StringComparison.OrdinalIgnoreCase)
        || cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
        || cultureName.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
        || cultureName.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLanguageMode(string languageMode) =>
        languageMode.Equals(SystemLanguageMode, StringComparison.OrdinalIgnoreCase)
            ? SystemLanguageMode
            : NormalizeCultureName(languageMode);

    private static string NormalizeDateFormatMode(string dateFormatMode) =>
        dateFormatMode.ToLowerInvariant() switch
        {
            DayMonthYearDateFormatMode => DayMonthYearDateFormatMode,
            MonthDayYearDateFormatMode => MonthDayYearDateFormatMode,
            YearMonthDayDateFormatMode => YearMonthDayDateFormatMode,
            _ => SystemDateFormatMode
        };

    private static string NormalizeCultureName(string cultureName) =>
        IsSimplifiedChinese(cultureName)
            ? SimplifiedChineseLanguage
            : EnglishLanguage;

    private static CultureInfo CaptureCulture(CultureInfo culture, string fallbackName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(
                string.IsNullOrWhiteSpace(culture.Name)
                    ? fallbackName
                    : culture.Name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(fallbackName);
        }
    }
}

using AttendanceList.Services;
using AttendanceList.Helpers;

namespace AttendanceList.Pages;

public sealed class AppRootPage : TabbedPage
{
    public AppRootPage(
        TodayPage today,
        HistoryPage history,
        ReportsPage reports,
        PeoplePage people,
        SettingsPage settings)
    {
        this.SetAppThemeColor(TabbedPage.BarBackgroundColorProperty, Ui.CardBackgroundLight, Ui.CardBackgroundDark);
        this.SetAppThemeColor(TabbedPage.BarTextColorProperty, Ui.TextSecondaryLight, Ui.TextSecondaryDark);
        SelectedTabColor = Color.FromArgb("#2563EB");
        this.SetAppThemeColor(
            TabbedPage.UnselectedTabColorProperty,
            Color.FromArgb("#64748B"),
            Color.FromArgb("#94A3B8"));

        Children.Add(Wrap(today, LocalizationService.T("Today")));
        Children.Add(Wrap(history, LocalizationService.T("History")));
        Children.Add(Wrap(reports, LocalizationService.T("Reports")));
        Children.Add(Wrap(people, LocalizationService.T("ClassesAndPeople")));
        Children.Add(Wrap(settings, LocalizationService.T("Settings")));
    }

    private static NavigationPage Wrap(Page page, string title)
    {
        page.Title = title;
        var navigation = new NavigationPage(page)
        {
            Title = title
        };
        navigation.SetAppThemeColor(
            NavigationPage.BarBackgroundColorProperty,
            Ui.CardBackgroundLight,
            Ui.CardBackgroundDark);
        navigation.SetAppThemeColor(
            NavigationPage.BarTextColorProperty,
            Ui.TextPrimaryLight,
            Ui.TextPrimaryDark);
        return navigation;
    }
}

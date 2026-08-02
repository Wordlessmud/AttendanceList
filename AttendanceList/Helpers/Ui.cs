using Microsoft.Maui.Controls.Shapes;

namespace AttendanceList.Helpers;

public static class Ui
{
    public static readonly Color PageBackgroundLight = Color.FromArgb("#F8FAFC");
    public static readonly Color PageBackgroundDark = Color.FromArgb("#020617");
    public static readonly Color CardBackgroundLight = Colors.White;
    public static readonly Color CardBackgroundDark = Color.FromArgb("#0F172A");
    public static readonly Color TextPrimaryLight = Color.FromArgb("#0F172A");
    public static readonly Color TextPrimaryDark = Color.FromArgb("#F8FAFC");
    public static readonly Color TextSecondaryLight = Color.FromArgb("#475569");
    public static readonly Color TextSecondaryDark = Color.FromArgb("#CBD5E1");
    public static readonly Color BorderLight = Color.FromArgb("#CBD5E1");
    public static readonly Color BorderDark = Color.FromArgb("#334155");
    public static readonly Color SecondaryButtonLight = Color.FromArgb("#E2E8F0");
    public static readonly Color SecondaryButtonDark = Color.FromArgb("#334155");

    public static Border Card(View content, Thickness? padding = null)
    {
        var border = new Border
        {
            Content = content,
            Padding = padding ?? new Thickness(16),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 }
        };
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, CardBackgroundLight, CardBackgroundDark);
        border.SetAppTheme(
            Border.StrokeProperty,
            new SolidColorBrush(BorderLight),
            new SolidColorBrush(BorderDark));
        return border;
    }

    public static Label Heading(string text, double size = 24)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = FontAttributes.Bold
        };
        label.SetAppThemeColor(Label.TextColorProperty, TextPrimaryLight, TextPrimaryDark);
        return label;
    }

    public static Label Secondary(string text = "")
    {
        var label = new Label { Text = text, FontSize = 14 };
        label.SetAppThemeColor(Label.TextColorProperty, TextSecondaryLight, TextSecondaryDark);
        return label;
    }

    public static Label Watermark(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 11,
            LineHeight = 1.15,
            Opacity = 0.48,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        label.SetAppThemeColor(
            Label.TextColorProperty,
            Color.FromArgb("#64748B"),
            Color.FromArgb("#94A3B8"));
        return label;
    }

    public static Button SecondaryButton(string text, string? description = null)
    {
        var button = new Button { Text = text };
        button.SetAppThemeColor(Button.BackgroundColorProperty, SecondaryButtonLight, SecondaryButtonDark);
        button.SetAppThemeColor(Button.TextColorProperty, TextPrimaryLight, TextPrimaryDark);
        if (!string.IsNullOrWhiteSpace(description))
        {
            SemanticProperties.SetDescription(button, description);
        }
        return button;
    }

    public static Button DestructiveSecondaryButton(string text, string? description = null)
    {
        var button = new Button
        {
            Text = text,
            BorderWidth = 1
        };
        button.SetAppThemeColor(Button.BackgroundColorProperty, Color.FromArgb("#FEF2F2"), Color.FromArgb("#450A0A"));
        button.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#B91C1C"), Color.FromArgb("#FCA5A5"));
        button.SetAppThemeColor(Button.BorderColorProperty, Color.FromArgb("#FCA5A5"), Color.FromArgb("#991B1B"));
        if (!string.IsNullOrWhiteSpace(description))
        {
            SemanticProperties.SetDescription(button, description);
        }
        return button;
    }

    public static async Task RunSafelyAsync(Page page, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Navigation and platform lifecycle changes may legitimately cancel a load.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            if (page.Window is not null)
            {
                await page.DisplayAlertAsync(
                    Services.LocalizationService.T("Error"),
                    exception.Message,
                    Services.LocalizationService.T("OK"));
            }
        }
    }
}

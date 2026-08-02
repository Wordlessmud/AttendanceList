namespace AttendanceList.Services;

public sealed class MotionService
{
    public bool IsReducedMotionEnabled
    {
        get
        {
            if (Preferences.Default.Get("reduce_motion", false))
            {
                return true;
            }
#if ANDROID
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is not null
                && Android.Provider.Settings.Global.GetFloat(
                    resolver,
                    Android.Provider.Settings.Global.AnimatorDurationScale,
                    1f) == 0f;
#elif WINDOWS
            return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return false;
#endif
        }
    }

    public async Task RemoveAsync(VisualElement view)
    {
        view.AbortAnimation("motion-remove");
        var duration = IsReducedMotionEnabled ? 90u : 140u;
        if (IsReducedMotionEnabled)
        {
            await view.FadeToAsync(0, duration, Easing.CubicOut);
            return;
        }
        await Task.WhenAll(
            view.FadeToAsync(0, duration, Easing.CubicOut),
            view.ScaleToAsync(0.97, duration, Easing.CubicOut));
    }

    public async Task EnterStepAsync(VisualElement view, int direction)
    {
        view.AbortAnimation("motion-step");
        view.Opacity = 0;
        view.TranslationX = IsReducedMotionEnabled ? 0 : Math.Sign(direction) * 18;
        var duration = IsReducedMotionEnabled ? 100u : 180u;
        if (IsReducedMotionEnabled)
        {
            await view.FadeToAsync(1, duration, Easing.CubicOut);
            return;
        }
        await Task.WhenAll(
            view.FadeToAsync(1, duration, Easing.CubicOut),
            view.TranslateToAsync(0, 0, duration, Easing.CubicOut));
    }

    public async Task ChangeStepAsync(VisualElement view, int direction, Action updateContent)
    {
        var signedDirection = Math.Sign(direction);
        if (IsReducedMotionEnabled)
        {
            await view.FadeToAsync(0, 70, Easing.CubicOut);
            updateContent();
            await view.FadeToAsync(1, 100, Easing.CubicOut);
            return;
        }

        await Task.WhenAll(
            view.FadeToAsync(0, 90, Easing.CubicIn),
            view.TranslateToAsync(-signedDirection * 12, 0, 90, Easing.CubicIn));
        updateContent();
        view.TranslationX = signedDirection * 18;
        await Task.WhenAll(
            view.FadeToAsync(1, 180, Easing.CubicOut),
            view.TranslateToAsync(0, 0, 180, Easing.CubicOut));
    }

    public async Task EmphasizeStateAsync(VisualElement view)
    {
        view.AbortAnimation("motion-state");
        view.Opacity = 0.65;
        view.Scale = IsReducedMotionEnabled ? 1 : 0.98;
        var duration = IsReducedMotionEnabled ? 90u : 160u;
        if (IsReducedMotionEnabled)
        {
            await view.FadeToAsync(1, duration, Easing.CubicOut);
            return;
        }
        await Task.WhenAll(
            view.FadeToAsync(1, duration, Easing.CubicOut),
            view.ScaleToAsync(1, duration, Easing.CubicOut));
    }
}

using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models;
#if WINDOWS
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
#endif

namespace SalmonEgg.Presentation.Services;

public sealed class UiRuntimeService : IUiRuntimeService
{
    private readonly IUiDispatcher _uiDispatcher;

#if WINDOWS
    private readonly UISettings _uiSettings = new();
    private bool _isAnimationsEnabledChangedSubscribed;
#endif

    public UiRuntimeService(IUiDispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
    }

    public void InitializeAnimations()
    {
#if WINDOWS
        ApplySystemAnimationsEnabled(_uiSettings.AnimationsEnabled);
        if (!_isAnimationsEnabledChangedSubscribed)
        {
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            _isAnimationsEnabledChangedSubscribed = true;
        }
#else
        ApplySystemAnimationsEnabled(true);
#endif
    }

    public void SetAnimationsEnabled(bool enabled)
    {
        UiMotionController.Current.IsAnimationEnabled = enabled;
        ApplyDependentAnimationPolicy();
    }

    public void ReloadShell()
    {
        App.ReloadMainShell();
    }

    private static void ApplySystemAnimationsEnabled(bool enabled)
    {
        UiMotionController.Current.IsSystemAnimationEnabled = enabled;
        ApplyDependentAnimationPolicy();
    }

    private static void ApplyDependentAnimationPolicy()
    {
#if WINDOWS
        Timeline.AllowDependentAnimations = UiMotionController.Current.IsEffectiveAnimationEnabled;
#endif
    }

#if WINDOWS
    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var enabled = sender.AnimationsEnabled;
        _uiDispatcher.Enqueue(() => ApplySystemAnimationsEnabled(enabled));
    }
#endif
}

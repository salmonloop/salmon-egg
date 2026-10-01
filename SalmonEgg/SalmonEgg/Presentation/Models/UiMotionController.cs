using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Animation;

namespace SalmonEgg.Presentation.Models;

public sealed partial class UiMotionController : ObservableObject
{
    public static UiMotionController Current { get; } = new();

    private bool _isAnimationEnabled = true;
    private bool _isSystemAnimationEnabled = true;

    /// <summary>
    /// User preference for whether application-owned motion is enabled.
    /// </summary>
    public bool IsAnimationEnabled
    {
        get => _isAnimationEnabled;
        set
        {
            if (SetProperty(ref _isAnimationEnabled, value))
            {
                NotifyMotionPolicyChanged();
            }
        }
    }

    /// <summary>
    /// System accessibility preference for whether UI animations are enabled.
    /// </summary>
    public bool IsSystemAnimationEnabled
    {
        get => _isSystemAnimationEnabled;
        set
        {
            if (SetProperty(ref _isSystemAnimationEnabled, value))
            {
                NotifyMotionPolicyChanged();
            }
        }
    }

    public bool IsEffectiveAnimationEnabled => IsAnimationEnabled && IsSystemAnimationEnabled;

    /// <summary>
    /// Native Frame navigation transition selected by the effective motion preference. Uno maps this WinUI API per platform.
    /// </summary>
    public NavigationTransitionInfo CreateNavigationTransitionInfo()
        => IsEffectiveAnimationEnabled
            ? new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();

    /// <summary>
    /// Transitions for small status icon changes.
    /// </summary>
    public TransitionCollection? StatusIconTransitions =>
        IsEffectiveAnimationEnabled ? CreateStatusIconTransitions() : null;

    private void NotifyMotionPolicyChanged()
    {
        OnPropertyChanged(nameof(IsEffectiveAnimationEnabled));
        OnPropertyChanged(nameof(StatusIconTransitions));
    }

    private static TransitionCollection CreateStatusIconTransitions()
    {
        return new TransitionCollection
        {
            new EntranceThemeTransition { FromVerticalOffset = 4 }
        };
    }
}

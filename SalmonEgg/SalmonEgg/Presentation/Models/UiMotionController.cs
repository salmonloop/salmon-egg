using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Animation;

namespace SalmonEgg.Presentation.Models;

public sealed partial class UiMotionController : ObservableObject
{
    public static UiMotionController Current { get; } = new();

    private bool _isAnimationEnabled = true;

    /// <summary>
    /// Single owner for whether application motion is enabled.
    /// </summary>
    public bool IsAnimationEnabled
    {
        get => _isAnimationEnabled;
        set
        {
            if (SetProperty(ref _isAnimationEnabled, value))
            {
                // Notify that all transition properties might have changed (from null to collection or vice versa)
                OnPropertyChanged(nameof(NavItemTransitions));
                OnPropertyChanged(nameof(ListItemTransitions));
                OnPropertyChanged(nameof(ToolCallTransitions));
                OnPropertyChanged(nameof(StatusIconTransitions));
            }
        }
    }

    /// <summary>
    /// Native Frame navigation transition selected by the global motion preference. Uno maps this WinUI API per platform.
    /// </summary>
    public NavigationTransitionInfo CreateNavigationTransitionInfo()
        => IsAnimationEnabled
            ? new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();

    /// <summary>
    /// Entrance transitions for sidebar items.
    /// </summary>
    public TransitionCollection? NavItemTransitions =>
        IsAnimationEnabled ? CreateEntranceTransitions(8, 0) : null;

    /// <summary>
    /// Standard list add/remove/reposition transitions.
    /// </summary>
    public TransitionCollection? ListItemTransitions =>
        IsAnimationEnabled ? CreateListTransitions() : null;

    /// <summary>
    /// Transitions for Tool Call expand/collapse and state changes.
    /// </summary>
    public TransitionCollection? ToolCallTransitions =>
        IsAnimationEnabled ? CreateToolCallTransitions() : null;

    /// <summary>
    /// Transitions for small status icon changes.
    /// </summary>
    public TransitionCollection? StatusIconTransitions =>
        IsAnimationEnabled ? CreateStatusIconTransitions() : null;

    private static TransitionCollection CreateEntranceTransitions(double fromHorizontal, double fromVertical)
    {
        return new TransitionCollection
        {
            new EntranceThemeTransition
            {
                FromHorizontalOffset = fromHorizontal,
                FromVerticalOffset = fromVertical
            }
        };
    }

    private static TransitionCollection CreateListTransitions()
    {
        return new TransitionCollection
        {
            new AddDeleteThemeTransition(),
            new RepositionThemeTransition()
        };
    }

    private static TransitionCollection CreateToolCallTransitions()
    {
        return new TransitionCollection
        {
            new EntranceThemeTransition { FromVerticalOffset = 8 },
            new RepositionThemeTransition()
        };
    }

    private static TransitionCollection CreateStatusIconTransitions()
    {
        return new TransitionCollection
        {
            new EntranceThemeTransition { FromVerticalOffset = 4 }
        };
    }
}

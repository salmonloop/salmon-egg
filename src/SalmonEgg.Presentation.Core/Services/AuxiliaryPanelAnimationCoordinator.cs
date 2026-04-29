namespace SalmonEgg.Presentation.Core.Services;

public enum AuxiliaryPanelAnimationPhase
{
    Hidden,
    Opening,
    Visible,
    Closing
}

public enum AuxiliaryPanelAnimationAction
{
    None,
    Show,
    Hide,
    StartOpening,
    StartClosing
}

public readonly record struct AuxiliaryPanelAnimationRequest(
    AuxiliaryPanelAnimationAction Action,
    double TravelDistance = 0);

public sealed class AuxiliaryPanelAnimationCoordinator
{
    private double _lastVisibleExtent;

    public AuxiliaryPanelAnimationCoordinator(bool initiallyVisible = false, double initialExtent = 0)
    {
        Phase = initiallyVisible ? AuxiliaryPanelAnimationPhase.Visible : AuxiliaryPanelAnimationPhase.Hidden;
        TargetVisible = initiallyVisible;
        if (initialExtent > 0)
        {
            _lastVisibleExtent = initialExtent;
        }
    }

    public AuxiliaryPanelAnimationPhase Phase { get; private set; }

    public bool TargetVisible { get; private set; }

    public AuxiliaryPanelAnimationRequest UpdateTarget(bool targetVisible, double extent, bool animationsEnabled)
    {
        RememberExtent(extent);
        TargetVisible = targetVisible;

        if (!animationsEnabled)
        {
            Phase = targetVisible ? AuxiliaryPanelAnimationPhase.Visible : AuxiliaryPanelAnimationPhase.Hidden;
            return new AuxiliaryPanelAnimationRequest(
                targetVisible ? AuxiliaryPanelAnimationAction.Show : AuxiliaryPanelAnimationAction.Hide);
        }

        return Phase switch
        {
            AuxiliaryPanelAnimationPhase.Hidden when targetVisible => StartOpeningOrShow(),
            AuxiliaryPanelAnimationPhase.Visible when !targetVisible => StartClosingOrHide(),
            _ => default
        };
    }

    public AuxiliaryPanelAnimationRequest CompleteAnimation()
    {
        return Phase switch
        {
            AuxiliaryPanelAnimationPhase.Opening => CompleteOpening(),
            AuxiliaryPanelAnimationPhase.Closing => CompleteClosing(),
            _ => default
        };
    }

    public void SnapTo(bool visible, double extent)
    {
        RememberExtent(extent);
        TargetVisible = visible;
        Phase = visible ? AuxiliaryPanelAnimationPhase.Visible : AuxiliaryPanelAnimationPhase.Hidden;
    }

    private AuxiliaryPanelAnimationRequest CompleteOpening()
    {
        if (TargetVisible)
        {
            Phase = AuxiliaryPanelAnimationPhase.Visible;
            return default;
        }

        return StartClosingOrHide();
    }

    private AuxiliaryPanelAnimationRequest CompleteClosing()
    {
        if (!TargetVisible)
        {
            Phase = AuxiliaryPanelAnimationPhase.Hidden;
            return new AuxiliaryPanelAnimationRequest(AuxiliaryPanelAnimationAction.Hide);
        }

        return StartOpeningOrShow();
    }

    private AuxiliaryPanelAnimationRequest StartOpeningOrShow()
    {
        var travelDistance = ResolveTravelDistance();
        if (travelDistance <= 0)
        {
            Phase = AuxiliaryPanelAnimationPhase.Visible;
            return new AuxiliaryPanelAnimationRequest(AuxiliaryPanelAnimationAction.Show);
        }

        Phase = AuxiliaryPanelAnimationPhase.Opening;
        return new AuxiliaryPanelAnimationRequest(AuxiliaryPanelAnimationAction.StartOpening, travelDistance);
    }

    private AuxiliaryPanelAnimationRequest StartClosingOrHide()
    {
        var travelDistance = ResolveTravelDistance();
        if (travelDistance <= 0)
        {
            Phase = AuxiliaryPanelAnimationPhase.Hidden;
            return new AuxiliaryPanelAnimationRequest(AuxiliaryPanelAnimationAction.Hide);
        }

        Phase = AuxiliaryPanelAnimationPhase.Closing;
        return new AuxiliaryPanelAnimationRequest(AuxiliaryPanelAnimationAction.StartClosing, travelDistance);
    }

    private void RememberExtent(double extent)
    {
        if (extent > 0)
        {
            _lastVisibleExtent = extent;
        }
    }

    private double ResolveTravelDistance() => _lastVisibleExtent;
}

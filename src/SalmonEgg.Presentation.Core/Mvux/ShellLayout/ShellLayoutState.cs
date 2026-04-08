namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public sealed record ShellLayoutState(
    WindowMetrics WindowMetrics,
    LayoutPadding TitleBarPadding,
    double TitleBarInsetsHeight,
    bool IsChatContext,
    RightPanelMode DesiredRightPanelMode,
    double RightPanelPreferredWidth,
    BottomPanelMode DesiredBottomPanelMode,
    double BottomPanelPreferredHeight,
    AuxiliaryPanelArea LastAuxiliaryPanelArea,
    double NavOpenPaneLength,
    double NavCompactPaneLength,
    bool? UserNavOpenIntent,
    bool IsMinimalPaneOpen)
{
    public static ShellLayoutState Default => new(
        new WindowMetrics(1280, 720, 1280, 720),
        new LayoutPadding(0, 0, 0, 0),
        48,
        false,
        RightPanelMode.None,
        320,
        BottomPanelMode.None,
        240,
        AuxiliaryPanelArea.None,
        300,
        72,
        null,
        false);
}

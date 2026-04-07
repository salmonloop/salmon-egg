namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public abstract record ShellLayoutAction;

public sealed record WindowMetricsChanged(double Width, double Height, double EffectiveWidth, double EffectiveHeight) : ShellLayoutAction;

public sealed record TitleBarInsetsChanged(double Left, double Right, double Height) : ShellLayoutAction;

public sealed record NavToggleRequested(string Source) : ShellLayoutAction;

public sealed record ContentContextChanged(bool IsChatContext) : ShellLayoutAction;

public sealed record ToggleRightPanelRequested(RightPanelMode TargetMode) : ShellLayoutAction;

public sealed record ToggleBottomPanelRequested : ShellLayoutAction;

public sealed record ClearAuxiliaryPanelsRequested : ShellLayoutAction;

public sealed record RightPanelModeChanged(RightPanelMode Mode) : ShellLayoutAction;

public sealed record BottomPanelModeChanged(BottomPanelMode Mode) : ShellLayoutAction;

public sealed record RightPanelResizeRequested(double AbsoluteWidth) : ShellLayoutAction;

public sealed record LeftNavResizeRequested(double OpenPaneLength) : ShellLayoutAction;

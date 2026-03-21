namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public readonly record struct LayoutPadding(double Left, double Top, double Right, double Bottom);

public enum NavigationPaneDisplayMode { Expanded, Compact, Minimal }

public enum RightPanelMode { None, Diff, Todo }

public enum BottomPanelMode { None, Dock }

public enum AuxiliaryPanelArea { None, Right, Bottom }

public readonly record struct WindowMetrics(double Width, double Height, double EffectiveWidth, double EffectiveHeight);

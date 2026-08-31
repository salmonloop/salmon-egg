namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record GamepadDiagnosticsSnapshot(
    bool IsSupported,
    int ConnectedGamepadCount,
    int ConnectedRawControllerCount,
    GamepadDiagnosticsInputSource InputSource,
    GamepadInputReading Reading,
    IReadOnlyCollection<GamepadNavigationIntent> ActiveIntents,
    IReadOnlyCollection<GamepadContextIntent> ActiveContextIntents,
    IReadOnlyCollection<GamepadShortcutIntent> ActiveShortcuts,
    IReadOnlyList<StandardGamepadDiagnostics> StandardGamepads,
    IReadOnlyList<RawGameControllerDiagnostics> RawControllers)
{
    public static GamepadDiagnosticsSnapshot Unsupported { get; } = new(
        IsSupported: false,
        ConnectedGamepadCount: 0,
        ConnectedRawControllerCount: 0,
        InputSource: GamepadDiagnosticsInputSource.None,
        Reading: default,
        ActiveIntents: [],
        ActiveContextIntents: [],
        ActiveShortcuts: [],
        StandardGamepads: [],
        RawControllers: []);
}

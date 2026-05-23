namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record GamepadDiagnosticsSnapshot(
    bool IsSupported,
    int ConnectedGamepadCount,
    int ConnectedRawControllerCount,
    GamepadDiagnosticsInputSource InputSource,
    GamepadInputReading Reading,
    IReadOnlyCollection<GamepadNavigationIntent> ActiveIntents,
    IReadOnlyList<RawGameControllerDiagnostics> RawControllers)
{
    public static GamepadDiagnosticsSnapshot Unsupported { get; } = new(
        IsSupported: false,
        ConnectedGamepadCount: 0,
        ConnectedRawControllerCount: 0,
        InputSource: GamepadDiagnosticsInputSource.None,
        Reading: default,
        ActiveIntents: [],
        RawControllers: []);
}

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record StandardGamepadDiagnostics(
    IReadOnlyList<string> ButtonLabels,
    IReadOnlyList<string> PressedButtons,
    GamepadInputReading Reading);

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record StandardGamepadDiagnostics(
    RawGameControllerFaceButtonLayout FaceButtonLayout,
    IReadOnlyList<string> ButtonLabels,
    IReadOnlyList<string> PressedButtons,
    GamepadInputReading Reading);

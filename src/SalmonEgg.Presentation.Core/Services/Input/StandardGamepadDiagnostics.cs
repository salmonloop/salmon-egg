namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record StandardGamepadDiagnostics(
    string DisplayName,
    ushort? HardwareVendorId,
    ushort? HardwareProductId,
    RawGameControllerFaceButtonLayout FaceButtonLayout,
    IReadOnlyList<string> ButtonLabels,
    IReadOnlyList<string> PressedButtons,
    GamepadInputReading Reading);

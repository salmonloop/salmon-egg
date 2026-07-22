namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed record RawGameControllerDiagnostics(
    string DisplayName,
    ushort HardwareVendorId,
    ushort HardwareProductId,
    bool IsWireless,
    int ButtonCount,
    int SwitchCount,
    int AxisCount,
    IReadOnlyList<string> PressedButtons,
    IReadOnlyList<string> ActiveSwitches,
    IReadOnlyList<double> Axes,
    GamepadInputReading Reading);

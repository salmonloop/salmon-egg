namespace SalmonEgg.Presentation.Core.Services.Input;

public readonly record struct BrowserGamepadButtonReading(
    bool Pressed,
    double Value);

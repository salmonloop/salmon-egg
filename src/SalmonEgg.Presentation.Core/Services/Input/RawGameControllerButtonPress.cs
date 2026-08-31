namespace SalmonEgg.Presentation.Core.Services.Input;

public readonly record struct RawGameControllerButtonPress(
    int Index,
    RawGameControllerButtonLabel Label);

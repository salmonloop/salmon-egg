namespace SalmonEgg.Presentation.Core.Services.Input;

public readonly record struct StandardGamepadFaceButtonLabels(
    RawGameControllerButtonLabel A = RawGameControllerButtonLabel.None,
    RawGameControllerButtonLabel B = RawGameControllerButtonLabel.None,
    RawGameControllerButtonLabel X = RawGameControllerButtonLabel.None,
    RawGameControllerButtonLabel Y = RawGameControllerButtonLabel.None);

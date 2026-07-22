namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerInputReadingMapper
{
    public static GamepadInputReading GetInputReading(
        IReadOnlyList<RawGameControllerButtonLabel> pressedButtonLabels,
        IReadOnlyList<GamepadDirectionalSwitchPosition> switches,
        IReadOnlyList<double> axes,
        RawGameControllerFaceButtonLayout faceButtonLayout = RawGameControllerFaceButtonLayout.Standard)
    {
        ArgumentNullException.ThrowIfNull(pressedButtonLabels);
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(axes);

        var reading = default(GamepadInputReading);

        foreach (var label in pressedButtonLabels)
        {
            reading = RawGameControllerButtonLabelMapper.Apply(label, reading, faceButtonLayout);
        }

        foreach (var position in switches)
        {
            reading = GamepadDirectionalSwitchMapper.Apply(position, reading);
        }

        if (axes.Count >= 2 && !RawGameControllerAxisNormalizer.IsAllAxesZero(axes))
        {
            reading = reading with
            {
                ThumbstickX = RawGameControllerAxisNormalizer.NormalizeHorizontal(axes[0]),
                ThumbstickY = RawGameControllerAxisNormalizer.NormalizeVertical(axes[1])
            };
        }

        return reading;
    }
}

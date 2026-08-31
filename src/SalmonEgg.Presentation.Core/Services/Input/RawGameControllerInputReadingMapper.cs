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

        var presses = new RawGameControllerButtonPress[pressedButtonLabels.Count];
        for (var i = 0; i < pressedButtonLabels.Count; i++)
        {
            presses[i] = new RawGameControllerButtonPress(Index: -1, Label: pressedButtonLabels[i]);
        }

        return GetInputReadingFromPresses(
            presses,
            switches,
            axes,
            faceButtonLayout,
            allowUnlabeledFaceIndexFallback: false);
    }

    public static GamepadInputReading GetInputReadingFromPresses(
        IReadOnlyList<RawGameControllerButtonPress> pressedButtons,
        IReadOnlyList<GamepadDirectionalSwitchPosition> switches,
        IReadOnlyList<double> axes,
        RawGameControllerFaceButtonLayout faceButtonLayout = RawGameControllerFaceButtonLayout.Standard,
        bool allowUnlabeledFaceIndexFallback = false,
        string? displayName = null,
        ushort hardwareVendorId = 0)
    {
        ArgumentNullException.ThrowIfNull(pressedButtons);
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(axes);

        var reading = default(GamepadInputReading);
        var labelsForLayout = new List<RawGameControllerButtonLabel>(pressedButtons.Count);
        foreach (var press in pressedButtons)
        {
            labelsForLayout.Add(press.Label);
        }

        var effectiveLayout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            faceButtonLayout,
            labelsForLayout);

        foreach (var press in pressedButtons)
        {
            if (press.Label != RawGameControllerButtonLabel.None)
            {
                reading = RawGameControllerButtonLabelMapper.Apply(press.Label, reading, effectiveLayout);
                continue;
            }

            if (allowUnlabeledFaceIndexFallback && press.Index >= 0)
            {
                reading = RawGameControllerUnlabeledFaceIndexPolicy.Apply(
                    press.Index,
                    reading,
                    displayName,
                    hardwareVendorId);
            }
        }

        foreach (var position in switches)
        {
            reading = GamepadDirectionalSwitchMapper.Apply(position, reading);
        }

        // Thumbstick idle must only inspect stick slots (0/1). Trigger travel on later
        // axes must not force centered-zero sticks through the bipolar normalizer.
        if (axes.Count >= 2
            && !RawGameControllerAxisNormalizer.AreStickAxesIdle(axes[0], axes[1]))
        {
            reading = reading with
            {
                ThumbstickX = RawGameControllerAxisNormalizer.NormalizeHorizontal(axes[0]),
                ThumbstickY = RawGameControllerAxisNormalizer.NormalizeVertical(axes[1])
            };
        }

        reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            reading,
            displayName,
            hardwareVendorId);

        return reading;
    }
}

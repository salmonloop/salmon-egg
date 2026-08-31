namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerFaceButtonLayoutResolver
{
    public static RawGameControllerFaceButtonLayout Resolve(
        string? displayName,
        ushort hardwareVendorId)
        => GamepadControllerIdentity.IsNintendo(displayName, hardwareVendorId)
            ? RawGameControllerFaceButtonLayout.Nintendo
            : RawGameControllerFaceButtonLayout.Standard;

    public static RawGameControllerFaceButtonLayout Resolve(StandardGamepadFaceButtonLabels labels)
    {
        if (IsLetterLabel(labels.A)
            || IsLetterLabel(labels.B)
            || IsLetterLabel(labels.X)
            || IsLetterLabel(labels.Y))
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        return RawGameControllerFaceButtonLayout.Standard;
    }

    public static RawGameControllerFaceButtonLayout Resolve(
        string? displayName,
        ushort? hardwareVendorId,
        StandardGamepadFaceButtonLabels labels)
    {
        if (hardwareVendorId is ushort vendorId)
        {
            if (Resolve(displayName, vendorId) == RawGameControllerFaceButtonLayout.Nintendo)
            {
                return RawGameControllerFaceButtonLayout.Nintendo;
            }
        }
        else if (Resolve(displayName, hardwareVendorId: 0) == RawGameControllerFaceButtonLayout.Nintendo)
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        return Resolve(labels);
    }

    public static RawGameControllerFaceButtonLayout Resolve(
        RawGameControllerFaceButtonLayout identityLayout,
        IReadOnlyList<RawGameControllerButtonLabel> pressedButtonLabels)
    {
        ArgumentNullException.ThrowIfNull(pressedButtonLabels);

        if (identityLayout == RawGameControllerFaceButtonLayout.Nintendo)
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        foreach (var label in pressedButtonLabels)
        {
            if (IsLetterLabel(label))
            {
                return RawGameControllerFaceButtonLayout.Nintendo;
            }
        }

        return identityLayout;
    }

    private static bool IsLetterLabel(RawGameControllerButtonLabel label)
        => label is RawGameControllerButtonLabel.LetterA
            or RawGameControllerButtonLabel.LetterB
            or RawGameControllerButtonLabel.LetterX
            or RawGameControllerButtonLabel.LetterY;
}

namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerFaceButtonLayoutResolver
{
    private const ushort NintendoVendorId = 0x057E;

    public static RawGameControllerFaceButtonLayout Resolve(
        string? displayName,
        ushort hardwareVendorId)
    {
        if (hardwareVendorId == NintendoVendorId)
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        if (ContainsToken(displayName, "Nintendo")
            || ContainsToken(displayName, "Switch Pro")
            || ContainsToken(displayName, "Joy-Con")
            || ContainsToken(displayName, "JoyCon"))
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        return RawGameControllerFaceButtonLayout.Standard;
    }

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

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}

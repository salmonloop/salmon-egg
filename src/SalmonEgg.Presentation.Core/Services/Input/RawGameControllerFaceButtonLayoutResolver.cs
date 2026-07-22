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

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}

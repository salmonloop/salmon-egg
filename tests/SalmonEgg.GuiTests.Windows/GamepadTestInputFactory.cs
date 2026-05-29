namespace SalmonEgg.GuiTests.Windows;

internal static class GamepadTestInputFactory
{
    private const string BackendEnvVar = "SALMONEGG_GUI_GAMEPAD_INPUT_BACKEND";

    public static IGamepadTestInput Create(WindowsGuiAppSession session)
    {
        var backend = Environment.GetEnvironmentVariable(BackendEnvVar);
        if (string.IsNullOrWhiteSpace(backend)
            || string.Equals(backend, "synthetic", StringComparison.OrdinalIgnoreCase))
        {
            return new SyntheticVirtualKeyGamepadTestInput(session);
        }

        if (string.Equals(backend, "native-device", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeDeviceGamepadTestInput();
        }

        throw new InvalidOperationException(
            $"Unsupported gamepad test backend '{backend}'. "
            + $"Supported values: 'synthetic', 'native-device'.");
    }
}

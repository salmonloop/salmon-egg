namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// App-facing face control intents used when mapping multi-brand physical face
/// keys for diagnostics inject and OS-path smoke. Core owns the family → physical
/// candidate list; platform injectors only resolve which candidate exists on the
/// active HID/virtual profile.
/// </summary>
public enum GamepadFaceSemantic
{
    Activate,
    Back,
    West,
    Voice
}

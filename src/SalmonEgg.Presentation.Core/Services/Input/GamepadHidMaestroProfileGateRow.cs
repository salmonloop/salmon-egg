namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// One confirmed HIDMaestro profile row for multi-profile OS-path gates.
/// Values are Core-owned family tokens and preferred physical face keys.
/// </summary>
public readonly record struct GamepadHidMaestroProfileGateRow(
    string ProfileId,
    string FamilyToken,
    string PreferredActivateKey,
    string PreferredBackKey,
    string PreferredWestKey,
    string PreferredVoiceKey);

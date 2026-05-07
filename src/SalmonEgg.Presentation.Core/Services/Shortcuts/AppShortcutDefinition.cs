namespace SalmonEgg.Presentation.Core.Services.Shortcuts;

public sealed record AppShortcutDefinition(
    string ActionId,
    string DisplayName,
    string DefaultGesture);

namespace SalmonEgg.Presentation.Core.Services.Input;

public readonly record struct GamepadInputReading(
    bool MoveUp,
    bool MoveDown,
    bool MoveLeft,
    bool MoveRight,
    bool Activate,
    bool Back,
    bool ShortcutVoiceToggle = false,
    double ThumbstickX = 0,
    double ThumbstickY = 0);

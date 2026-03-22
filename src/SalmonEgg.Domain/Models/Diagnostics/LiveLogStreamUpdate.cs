namespace SalmonEgg.Domain.Models.Diagnostics;

public sealed class LiveLogStreamUpdate
{
    public LiveLogStreamUpdate(
        string? currentLogFilePath,
        string appendedText,
        bool hasFileSwitched)
    {
        CurrentLogFilePath = currentLogFilePath;
        AppendedText = appendedText;
        HasFileSwitched = hasFileSwitched;
    }

    public string? CurrentLogFilePath { get; }

    public string AppendedText { get; }

    public bool HasFileSwitched { get; }
}

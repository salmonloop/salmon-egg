namespace SalmonEgg.Presentation.Core.Services.Input;

public enum VoiceInputPermissionStatus
{
    Granted = 0,
    Denied = 1,
    Unsupported = 2
}

public readonly record struct VoiceInputPermissionResult(
    VoiceInputPermissionStatus Status,
    string? Message = null,
    bool RequiresAuthorization = false)
{
    public bool IsGranted => Status == VoiceInputPermissionStatus.Granted;

    public static VoiceInputPermissionResult Granted() =>
        new(VoiceInputPermissionStatus.Granted);
}

public readonly record struct VoiceInputSessionOptions(
    string RequestId,
    string LanguageTag,
    bool EnablePartialResults = true,
    bool PreferOffline = false);

public readonly record struct VoiceInputPartialResult(
    string RequestId,
    string Text);

public readonly record struct VoiceInputFinalResult(
    string RequestId,
    string Text);

public readonly record struct VoiceInputSessionEndedResult(
    string RequestId);

public readonly record struct VoiceInputErrorResult(
    string RequestId,
    string Message,
    string? ErrorCode = null,
    bool RequiresAuthorization = false);

public sealed class VoiceInputStartFailureException : Exception
{
    public VoiceInputStartFailureException(string message, bool requiresAuthorization = false, Exception? innerException = null)
        : base(message, innerException)
    {
        RequiresAuthorization = requiresAuthorization;
    }

    public bool RequiresAuthorization { get; }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IVoiceInputService : IDisposable
{
    bool IsSupported { get; }

    bool IsListening { get; }

    event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

    event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

    event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

    event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

    Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default);

    Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a voice-input session and returns only after the native recognizer
    /// is ready to receive audio for the supplied request.
    /// </summary>
    Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the current voice-input session from the product perspective.
    /// Implementations may continue internal cleanup after this call is issued,
    /// but callers must not be required to keep the front-end voice session alive
    /// while waiting for native recognizer teardown.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

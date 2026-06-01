using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class NoOpVoiceInputService : IVoiceInputService, IVoiceInputRuntimeDiagnosticsSource
{
    public static NoOpVoiceInputService Instance { get; } = new();

    private NoOpVoiceInputService()
    {
    }

    public bool IsSupported => false;

    public bool IsListening => false;

    public event EventHandler<VoiceInputPartialResult>? PartialResultReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<VoiceInputFinalResult>? FinalResultReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded
    {
        add { }
        remove { }
    }

    public event EventHandler<VoiceInputErrorResult>? ErrorOccurred
    {
        add { }
        remove { }
    }

    public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new VoiceInputPermissionResult(VoiceInputPermissionStatus.Unsupported, "Voice input is not supported on this platform."));

    public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<VoiceInputRuntimeDiagnostics> GetRuntimeDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new VoiceInputRuntimeDiagnostics(null, null, null));

    public void Dispose()
    {
    }
}

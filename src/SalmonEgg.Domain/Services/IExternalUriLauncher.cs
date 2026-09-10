using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IExternalUriLauncher
{
    bool IsSupported { get; }

    // Called synchronously from the consent command until the platform has consumed user activation.
    Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken);
}

public enum ExternalUriOpenResult
{
    Opened,
    // The platform accepted the navigation intent but cannot report whether a page opened.
    Dispatched,
    Unavailable,
    Blocked,
    Failed,
    Cancelled
}

public sealed class UnsupportedExternalUriLauncher : IExternalUriLauncher
{
    public static UnsupportedExternalUriLauncher Instance { get; } = new();

    public bool IsSupported => false;

    public Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken)
        => Task.FromResult(ExternalUriOpenResult.Unavailable);
}

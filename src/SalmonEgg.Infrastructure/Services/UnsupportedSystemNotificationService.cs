using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class UnsupportedSystemNotificationService
    : ISystemNotificationService, ISystemNotificationActivationSource
{
    /// <summary>Never raised: a platform that cannot post a notification cannot report a tap either.</summary>
    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    public void Start()
    {
        // Referencing the event keeps "never raised" explicit rather than looking like an oversight.
        _ = Activated;
    }

    public bool IsSupported => false;

    public Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SystemNotificationPermissionResult.Unsupported);
    }

    public Task<SystemNotificationResult> ShowAsync(
        SystemNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(SystemNotificationResult.Unsupported);
    }
}

using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public enum SystemNotificationResult
{
    Shown,
    Denied,
    Unsupported,
    Failed
}

public enum SystemNotificationPermissionResult
{
    Granted,
    Denied,
    Unsupported,
    Failed
}

/// <summary>
/// Owns the platform boundary for system-native user notifications.
/// </summary>
public interface ISystemNotificationService
{
    bool IsSupported { get; }

    Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default);

    Task<SystemNotificationResult> ShowAsync(
        SystemNotificationRequest request,
        CancellationToken cancellationToken = default);
}

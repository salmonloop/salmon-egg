namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Authoritative user preference for completion notifications.
/// </summary>
public interface IApplicationNotificationSettings
{
    bool SystemNotificationsEnabled { get; }
}

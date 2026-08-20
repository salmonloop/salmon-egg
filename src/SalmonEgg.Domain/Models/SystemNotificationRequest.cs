namespace SalmonEgg.Domain.Models;

/// <summary>
/// Safe, platform-neutral content for a user-facing system notification.
/// </summary>
/// <remarks>
/// The payload intentionally carries no conversation content, prompt text or routing arguments:
/// nothing consumes a notification activation yet, so a routing payload would only advertise
/// behaviour the platforms do not implement. <see cref="NotificationId"/> is a stable per-turn
/// identity so platforms can replace rather than stack a repeated notification.
/// </remarks>
public sealed record SystemNotificationRequest(
    string NotificationId,
    string Title,
    string Body)
{
    public bool IsValid
        => !string.IsNullOrWhiteSpace(NotificationId)
            && !string.IsNullOrWhiteSpace(Title)
            && !string.IsNullOrWhiteSpace(Body);
}

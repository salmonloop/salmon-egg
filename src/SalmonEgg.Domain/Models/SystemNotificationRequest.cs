namespace SalmonEgg.Domain.Models;

/// <summary>
/// Safe, platform-neutral content for a user-facing system notification.
/// </summary>
/// <remarks>
/// The payload carries no conversation content or prompt text. <see cref="NotificationId"/> is a
/// stable per-turn identity so platforms can replace rather than stack a repeated notification, and
/// <see cref="ConversationId"/> is what a tapped notification routes to.
/// </remarks>
public sealed record SystemNotificationRequest(
    string NotificationId,
    string Title,
    string Body,
    string? ConversationId = null)
{
    public bool IsValid
        => !string.IsNullOrWhiteSpace(NotificationId)
            && !string.IsNullOrWhiteSpace(Title)
            && !string.IsNullOrWhiteSpace(Body);
}

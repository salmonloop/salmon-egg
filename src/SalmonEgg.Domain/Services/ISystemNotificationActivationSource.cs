using System;

namespace SalmonEgg.Domain.Services;

public sealed class SystemNotificationActivatedEventArgs : EventArgs
{
    public SystemNotificationActivatedEventArgs(string notificationId, string? conversationId)
    {
        NotificationId = notificationId;
        ConversationId = conversationId;
    }

    public string NotificationId { get; }

    /// <summary>The conversation the notification was about, when it carried one.</summary>
    public string? ConversationId { get; }
}

/// <summary>
/// Reports that the user interacted with a system notification this app posted.
/// </summary>
/// <remarks>
/// Implemented alongside the platform notification service, because the same native handle both posts
/// notifications and reports activations. Listening starts on an explicit <see cref="Start"/> call
/// rather than at construction, so dependency injection stays free of platform side effects.
/// </remarks>
public interface ISystemNotificationActivationSource
{
    event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    /// <summary>Begins listening. Idempotent; the application startup workflow owns the single call.</summary>
    void Start();
}

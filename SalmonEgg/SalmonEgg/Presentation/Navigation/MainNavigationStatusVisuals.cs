using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Navigation;

public static class MainNavigationStatusVisuals
{
    // Segoe Fluent Icons names: Comment, Shield, Help and Warning.
    // https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
    public static string Glyph(ConversationStatusIcon icon) => icon switch
    {
        ConversationStatusIcon.Permission => "\uEA18",
        ConversationStatusIcon.Input => "\uE897",
        ConversationStatusIcon.Error => "\uE7BA",
        _ => "\uE90A"
    };

    public static bool IsProgressActive(ConversationStatusIcon icon, bool isPlaceholder)
        => isPlaceholder || icon == ConversationStatusIcon.Working;

    public static Visibility ShowProgress(ConversationStatusIcon icon, bool isPlaceholder)
        => IsProgressActive(icon, isPlaceholder) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowGlyph(ConversationStatusIcon icon, bool isPlaceholder)
        => IsProgressActive(icon, isPlaceholder) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowUnread(ConversationStatusIcon icon)
        => icon == ConversationStatusIcon.Unread ? Visibility.Visible : Visibility.Collapsed;
}

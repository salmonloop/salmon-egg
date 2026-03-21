using System;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed class ChatShellViewModel
{
    public ChatShellViewModel(ChatViewModel chat, ShellLayoutViewModel shellLayout)
    {
        Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        ShellLayout = shellLayout ?? throw new ArgumentNullException(nameof(shellLayout));
    }

    public ChatViewModel Chat { get; }

    public ShellLayoutViewModel ShellLayout { get; }
}

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Controls;

public sealed partial class ChatInputArea : UserControl
{
    public event EventHandler? PromptSubmitted;

    public ChatViewModel ViewModel { get; }

    public ChatInputArea()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
        InitializeComponent();
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.ShowSlashCommands)
        {
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Tab:
                if (ViewModel.TryAcceptSelectedSlashCommand())
                {
                    if (sender is TextBox tb)
                    {
                        tb.SelectionStart = tb.Text?.Length ?? 0;
                    }
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Up:
                if (ViewModel.TryMoveSlashSelection(-1))
                {
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Down:
                if (ViewModel.TryMoveSlashSelection(1))
                {
                    e.Handled = true;
                    return;
                }
                break;
        }
    }

    private void OnSendAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Enter sends; if slash commands menu is open, accept selection instead.
        if (ViewModel.ShowSlashCommands)
        {
            if (ViewModel.TryAcceptSelectedSlashCommand())
            {
                InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
                args.Handled = true;
                return;
            }
        }

        TrySendPrompt();
        args.Handled = true;
    }

    private void OnNewLineAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ctrl+Enter inserts a newline at the caret (even if IME/target platform behaves differently).
        var text = InputBox.Text ?? string.Empty;
        var start = InputBox.SelectionStart;
        var length = InputBox.SelectionLength;

        var newline = Environment.NewLine;
        if (length > 0)
        {
            text = text.Remove(start, length);
        }
        text = text.Insert(start, newline);

        InputBox.Text = text;
        InputBox.SelectionStart = start + newline.Length;
        InputBox.SelectionLength = 0;

        args.Handled = true;
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        TrySendPrompt();
    }

    private void TrySendPrompt()
    {
        if (ViewModel.SendPromptCommand != null && ViewModel.SendPromptCommand.CanExecute(null))
        {
            ViewModel.SendPromptCommand.Execute(null);
            PromptSubmitted?.Invoke(this, EventArgs.Empty);
        }
    }
}


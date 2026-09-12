using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>Opt-in load producer. Only the production transcript observer may acknowledge its replies.</summary>
internal static class ReadReceiptProbeDriver
{
    private const string RemoteSessionId = "read-receipt-probe-session";
#if DEBUG
    private static int _started;
#endif

    public static void TryStart(IServiceProvider services, DependencyObject shellRoot)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("SALMONEGG_READ_RECEIPT_PROBE") != "1"
            || Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = RunAsync(services, shellRoot);
#endif
    }

#if DEBUG
    private static async Task RunAsync(IServiceProvider services, DependencyObject shellRoot)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var chat = services.GetRequiredService<IChatStore>();
        var attention = services.GetRequiredService<IConversationAttentionStore>();
        var viewModel = services.GetRequiredService<ChatViewModel>();
        var activation = services.GetRequiredService<AppActivationSignalSource>();
        var shutdown = services.GetRequiredService<IApplicationShutdownProgress>();
        void OnShutdown(object? sender, PropertyChangedEventArgs args) { if (shutdown.IsShuttingDown) stop.Cancel(); }
        shutdown.PropertyChanged += OnShutdown;
        var completed = 0;
        var passed = false;
        try
        {
            App.BootLog("ReadReceiptProbe: started");
            await WaitUntilAsync(() => viewModel.CurrentSessionId is not null, stop.Token);
            await services.GetRequiredService<MainNavigationViewModel>().ActivateSessionAsync(viewModel.CurrentSessionId!, null)
                .WaitAsync(TimeSpan.FromSeconds(8), stop.Token);
            await WaitUntilAsync(() => viewModel.CurrentSessionId is not null && !viewModel.IsActivationOverlayVisible
                && activation.IsActive && FindMessages(shellRoot) is { IsLoaded: true, ActualHeight: > 0 }, stop.Token);
            var list = FindMessages(shellRoot)!;
            var conversationId = viewModel.CurrentSessionId!;
            var connectionId = "read-receipt-probe";
            // Explicit protocol-shaped fixture facts exercise the production UI projection only.
            // No ACP endpoint is started, so this probe does not claim transport or recovery coverage.
            await chat.Dispatch(new SetBindingSliceAction(new(conversationId, RemoteSessionId, null)));
            await chat.Dispatch(new SetConversationRuntimeStateAction(new(conversationId, ConversationRuntimePhase.Warm,
                connectionId, RemoteSessionId, null, ConversationRuntimeReasons.MarkedHydrated, DateTime.UtcNow)));
            await chat.Dispatch(new BeginTurnAction(conversationId, "read-receipt-probe-turn", ChatTurnPhase.Responding,
                RemoteSessionId: RemoteSessionId, ConnectionInstanceId: connectionId));

            foreach (var (name, text) in new[]
            {
                ("plain", "Read receipt alpha."),
                ("same-size", "Read receipt bravo.")
            })
            {
                var message = CreateMessage(text);
                var version = await MarkAsync(attention, conversationId, connectionId, message);
                await chat.Dispatch(new HydrateConversationAction(conversationId, ImmutableList.Create(message),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
                await WaitForReadAsync(attention, conversationId, version, message, stop.Token);
                completed++;
                App.BootLog($"ReadReceiptProbe case={name} version={version} unread=False active={activation.IsActive} passed=True");
            }

            await VerifyModalAsync(chat, attention, list, conversationId, connectionId, activation, stop.Token);
            completed += 2;
            await VerifyMinimizedAsync(chat, attention, viewModel, conversationId, connectionId, activation, stop.Token);
            completed += 2;

            var markdown = CreateMessage("**Read receipt markdown.**");
            var markdownVersion = await MarkAsync(attention, conversationId, connectionId, markdown);
            await chat.Dispatch(new HydrateConversationAction(conversationId, ImmutableList.Create(markdown),
                ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
            await WaitForReadAsync(attention, conversationId, markdownVersion, markdown, stop.Token);
            completed++;
            App.BootLog($"ReadReceiptProbe case=markdown version={markdownVersion} unread=False active={activation.IsActive} passed=True");

            var longMessage = CreateMessage(string.Join('\n', Enumerable.Range(1, 60).Select(index => $"Reading line {index:D2}.")));
            await chat.Dispatch(new HydrateConversationAction(conversationId, ImmutableList.Create(longMessage),
                ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
            await WaitUntilAsync(() => FindScrollViewer(list) is { ScrollableHeight: > 200 }, stop.Token);
            var scroll = FindScrollViewer(list)!;
            scroll.ChangeView(null, 0, null, disableAnimation: true);
            await WaitUntilAsync(() => scroll.VerticalOffset < 1 && scroll.ScrollableHeight > scroll.ViewportHeight, stop.Token);
            var longVersion = await MarkAsync(attention, conversationId, connectionId, longMessage);
            await Task.Delay(500, stop.Token);
            var detached = (await attention.GetCurrentStateAsync()).Conversations[conversationId];
            if (!detached.HasUnread || detached.UnreadVersion != longVersion || scroll.VerticalOffset >= scroll.ScrollableHeight)
                throw new InvalidOperationException("Detached long reply was acknowledged before its end became visible.");
            App.BootLog($"ReadReceiptProbe case=detached version={longVersion} unread=True active={activation.IsActive} offset={scroll.VerticalOffset:F1} passed=True");
            completed++;
            scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
            await WaitForReadAsync(attention, conversationId, longVersion, longMessage, stop.Token);
            App.BootLog($"ReadReceiptProbe case=scroll-end version={longVersion} unread=False active={activation.IsActive} passed=True");
            completed++;
            passed = true;
        }
        catch (Exception error)
        {
            App.BootLog($"ReadReceiptProbe: faulted {error}");
        }
        finally
        {
            shutdown.PropertyChanged -= OnShutdown;
            App.BootLog($"ReadReceiptProbe: complete cases={completed} passed={passed}");
        }
    }

    private static ConversationMessageSnapshot CreateMessage(string text)
        => new() { Id = "read-receipt-probe-message", ContentType = "text", TextContent = text, IsOutgoing = false, Timestamp = DateTime.UtcNow };

    private static async Task VerifyModalAsync(IChatStore chat, IConversationAttentionStore attention, ListView list,
        string conversationId, string connectionId, AppActivationSignalSource activation, CancellationToken cancellationToken)
    {
        var dialog = new ContentDialog { Title = "Read receipt probe", Content = "This native dialog covers the reply.", CloseButtonText = "Close" };
        var root = list.XamlRoot ?? throw new InvalidOperationException("The transcript has no native XamlRoot.");
        ContentDialogHost.AttachToXamlRoot(dialog, root);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += (_, _) => opened.TrySetResult();
        var shown = dialog.ShowAsync();
        var message = CreateMessage("Read receipt behind a modal.");
        var version = 0;
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.IsOpen))
                throw new InvalidOperationException("The modal fixture did not open a native popup.");
            version = await MarkAsync(attention, conversationId, connectionId, message);
            await chat.Dispatch(new HydrateConversationAction(conversationId, ImmutableList.Create(message),
                ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
            await WaitUntilAsync(() => FindDescendant<TextBlock>(list, text => text.Text == message.TextContent) is { ActualHeight: > 0 }, cancellationToken);
            await AssertUnreadForIntervalAsync(attention, conversationId, version, () => activation.IsActive
                && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.IsOpen), cancellationToken);
            App.BootLog($"ReadReceiptProbe case=modal version={version} unread=True active={activation.IsActive} passed=True");
        }
        finally
        {
            dialog.Hide();
            await shown;
        }
        await WaitForReadAsync(attention, conversationId, version, message, cancellationToken);
        App.BootLog($"ReadReceiptProbe case=modal-closed version={version} unread=False active={activation.IsActive} passed=True");
    }

    private static async Task VerifyMinimizedAsync(IChatStore chat, IConversationAttentionStore attention, ChatViewModel viewModel,
        string conversationId, string connectionId, AppActivationSignalSource activation, CancellationToken cancellationToken)
    {
        var window = activation.ActiveWindow;
        App.BootLog("ReadReceiptProbe ready=minimize");
        try
        {
            await WaitUntilAsync(() => !activation.IsActive, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            App.BootLog($"ReadReceiptProbe native-minimize active={activation.IsActive} visible={window?.Visible} rootVisible={(window?.Content as FrameworkElement)?.XamlRoot?.IsHostVisible} presenter={window?.AppWindow.Presenter.GetType().Name}");
            throw;
        }
        var message = CreateMessage("Read receipt while minimized.");
        var version = await MarkAsync(attention, conversationId, connectionId, message);
        await chat.Dispatch(new HydrateConversationAction(conversationId, ImmutableList.Create(message),
            ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
        await WaitUntilAsync(() => viewModel.MessageHistory.Any(item => ReferenceEquals(item.PresentedSnapshot, message)), cancellationToken);
        await AssertUnreadForIntervalAsync(attention, conversationId, version, () => !activation.IsActive, cancellationToken);
        App.BootLog($"ReadReceiptProbe case=minimized version={version} unread=True active={activation.IsActive} passed=True");
        App.BootLog("ReadReceiptProbe ready=restore");
        await WaitUntilAsync(() => activation.IsActive, cancellationToken);
        await WaitForReadAsync(attention, conversationId, version, message, cancellationToken);
        App.BootLog($"ReadReceiptProbe case=restored version={version} unread=False active={activation.IsActive} passed=True");
    }

    private static async Task AssertUnreadForIntervalAsync(IConversationAttentionStore attention, string conversationId,
        int version, Func<bool> hasNativePrecondition, CancellationToken cancellationToken)
    {
        // A bounded negative-observation window is test pacing, not the production read rule.
        // Every sample also checks the native condition and this exact content version.
        for (var sample = 0; sample < 12; sample++)
        {
            var state = await attention.GetCurrentStateAsync();
            if (!hasNativePrecondition() || !state.TryGetConversation(conversationId, out var pending)
                || pending is not { HasUnread: true } || pending.UnreadVersion != version)
                throw new InvalidOperationException("Hidden reply was acknowledged or the native hiding condition was lost.");
            await Task.Delay(25, cancellationToken);
        }
    }

    private static async Task<int> MarkAsync(IConversationAttentionStore attention, string conversationId,
        string connectionId, ConversationMessageSnapshot message)
    {
        await attention.Dispatch(new MarkConversationUnreadAction(conversationId, ConversationAttentionSource.AgentMessage,
            DateTime.UtcNow, null, RemoteSessionId, message, connectionId));
        return (await attention.GetCurrentStateAsync()).Conversations[conversationId].UnreadVersion;
    }

    private static async Task WaitForReadAsync(IConversationAttentionStore attention, string conversationId,
        int version, ConversationMessageSnapshot message, CancellationToken cancellationToken)
    {
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = attention.State.ForEach((state, _) =>
        {
            if (state is not null && state.TryGetConversation(conversationId, out var item) && item is { HasUnread: false }
                && item.UnreadVersion == version && ReferenceEquals(item.Content, message)) read.TrySetResult();
            return ValueTask.CompletedTask;
        }, out var subscription);
        try
        {
            var current = await attention.GetCurrentStateAsync();
            if (current.TryGetConversation(conversationId, out var item) && item is { HasUnread: false }
                && item.UnreadVersion == version && ReferenceEquals(item.Content, message)) read.TrySetResult();
            await read.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally { subscription?.Dispose(); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(25, budget.Token);
    }

    private static ListView? FindMessages(DependencyObject root)
        => FindDescendant<ListView>(root, list => AutomationProperties.GetAutomationId(list) == "ChatView.MessagesList");

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
        => FindDescendant<ScrollViewer>(root, static _ => true);

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : class, DependencyObject
    {
        if (root is T target && predicate(target)) return target;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (found is not null) return found;
        }
        return null;
    }
#endif
}

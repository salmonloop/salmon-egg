using System;
using Microsoft.UI.Xaml;

#if DEBUG && __UNO_SKIA__
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.Views.Chat;
using Windows.Foundation;
#endif

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>Reads the production elicitation card; an external XTest driver supplies all input.</summary>
internal static class NativeElicitationProbeDriver
{
#if DEBUG && __UNO_SKIA__
    private static int _started;
#endif

    public static void TryStart(IServiceProvider services, DependencyObject shellRoot)
    {
#if DEBUG && __UNO_SKIA__
        if (Environment.GetEnvironmentVariable("SALMONEGG_NATIVE_ELICITATION_PROBE") != "1"
            || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("NativeElicitationProbe");
        _ = RunAsync(services.GetRequiredService<MainNavigationViewModel>(), shellRoot, logger);
#endif
    }

#if DEBUG && __UNO_SKIA__
    private static async Task RunAsync(MainNavigationViewModel navigation, DependencyObject root, ILogger logger)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            if (!await navigation.ActivateSessionAsync("native-elicitation-conversation", "native-elicitation-project")
                .WaitAsync(deadline.Token).ConfigureAwait(true))
            {
                throw new InvalidOperationException("The seeded ACP conversation could not be activated.");
            }

            var sequence = 0;
            while (!deadline.IsCancellationRequested)
            {
                if (Find<ChatView>(root, static view => view.IsLoaded) is { } view)
                {
                    Observe(view, logger, ++sequence);
                }

                await Task.Delay(100, deadline.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            logger.LogInformation("NativeElicitationProbe expired pid={ProcessId}", Environment.ProcessId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "NativeElicitationProbe failed pid={ProcessId}", Environment.ProcessId);
        }
    }

    private static void Observe(ChatView view, ILogger logger, int sequence)
    {
        var request = view.ViewModel.PendingElicitationRequest;
        // Only fixed fixture labels may enter diagnostics. Never print the URL, prompt or form values.
        var stage = request?.Prompt switch
        {
            "native-url-decline" => "decline",
            "native-url-cancel" => "cancel",
            "native-url-open" => "open",
            "native-url-expire" => "expire",
            _ => "none"
        };
        var host = Find<Border>(view, static element => AutomationProperties.GetAutomationId(element) == "ElicitationHost"
            && IsVisible(element));
        var fullUrl = host is null ? null : Find<TextBlock>(host,
            static element => AutomationProperties.GetAutomationId(element) == "Elicitation.FullUrl" && IsVisible(element));
        var hostname = host is null ? null : Find<TextBlock>(host,
            static element => AutomationProperties.GetAutomationId(element) == "Elicitation.UrlHost" && IsVisible(element));
        logger.LogInformation(
            "NativeElicitationProbe sample pid={ProcessId} seq={Sequence} stage={Stage} visible={Visible} url={UrlMatches} host={HostMatches} completed={Completed} error={Error}",
            Environment.ProcessId, sequence, stage, host is not null,
            request is not null && fullUrl?.Text == request.FullUrl && request.FullUrl.Length > 0,
            request is not null && hostname?.Text == request.UrlHost && request.UrlHost.Length > 0,
            request?.IsCompleted ?? false, request?.HasError ?? false);
        if (host is null || request is null || stage == "none") return;
        ObserveButton(host, request.CancelCommand, "cancel", sequence, logger);
        ObserveButton(host, request.DeclineCommand, "decline", sequence, logger);
        ObserveButton(host, request.SubmitCommand, "submit", sequence, logger);
        ObserveButton(host, request.ReopenCommand, "reopen", sequence, logger);
        ObserveButton(host, request.DismissCommand, "dismiss", sequence, logger);
    }

    private static void ObserveButton(DependencyObject host, System.Windows.Input.ICommand command, string action,
        int sequence, ILogger logger)
    {
        if (Find<Button>(host, element => ReferenceEquals(element.Command, command) && IsVisible(element)) is not { } button)
            return;
        var bounds = button.TransformToVisual(null).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        var scale = button.XamlRoot!.RasterizationScale;
        logger.LogInformation(
            "NativeElicitationProbe button seq={Sequence} action={Action} enabled={Enabled} x={X} y={Y} width={Width} height={Height} rootHeight={RootHeight} scale={Scale}",
            sequence, action, button.IsEnabled,
            (int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale),
            (int)Math.Round(button.XamlRoot.Size.Height * scale), scale);
    }

    private static bool IsVisible(FrameworkElement element)
    {
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return false;
        return true;
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> matches) where T : class, DependencyObject
    {
        if (root is T candidate && matches(candidate)) return candidate;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (Find(VisualTreeHelper.GetChild(root, index), matches) is { } found) return found;
        return null;
    }
#endif
}

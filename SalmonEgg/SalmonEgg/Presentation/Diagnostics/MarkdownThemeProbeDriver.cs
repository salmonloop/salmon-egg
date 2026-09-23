using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Controls;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>
/// Diagnostics-only runtime probe for the Skia Markdown ThemeListener lifecycle regression.
/// </summary>
/// <remarks>
/// The Toolkit ThemeListener historically dereferenced <c>Window.Current.CoreWindow</c>, which does
/// not exist on Skia desktop windows; loading or disposing a Markdown control could then raise a
/// dispatcher exception. The driver exercises the real seeded Markdown conversation end to end —
/// materialized presenter, theme toggle, native window reactivation, authoritative selection switch
/// away (unload) and back (reload) — and reports only observable UI facts. It is compiled out of
/// Release behavior and inert unless <c>SALMONEGG_MARKDOWN_THEME_PROBE=1</c>.
/// </remarks>
internal static class MarkdownThemeProbeDriver
{
    private const string EnableVariable = "SALMONEGG_MARKDOWN_THEME_PROBE";

    // Mirrors SkiaDesktopGuiSeedWriter's constants; the app cannot reference the test-support
    // assembly, and the shell gate hardcodes the same ids when seeding.
    private const string MixedConversationId = "skia-mixed-session-01";
    private const string PlainConversationId = "skia-plain-session-02";
    private const string ProjectId = "project-1";

    private const int StepTimeoutMilliseconds = 15000;
    private const int PollDelayMilliseconds = 50;

#if DEBUG && __UNO_SKIA__
    private static int _started;
#endif

    public static void TryStart(IServiceProvider services, DependencyObject shellRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(shellRoot);

#if DEBUG && __UNO_SKIA__
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal)
            || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var navigation = services.GetRequiredService<MainNavigationViewModel>();
        _ = RunAsync(navigation, shellRoot);
#endif
    }

#if DEBUG && __UNO_SKIA__
    private static async Task RunAsync(MainNavigationViewModel navigation, DependencyObject shellRoot)
    {
        var loaded = false;
        var themeToggled = false;
        var reactivated = false;
        var unloaded = false;
        var reloaded = false;

        try
        {
            App.BootLog("MarkdownThemeProbe: started");

            // The NumberBox probe drives the content frame into settings and back; it owns the
            // frame until it completes. Wait for it instead of racing it for navigation.
            await NumberBoxThemeProbeDriver.Completion.WaitAsync(TimeSpan.FromSeconds(120)).ConfigureAwait(true);

            // Re-assert the seeded conversation through the authoritative selection so the chat
            // transcript is on screen regardless of where the frame was left.
            if (!await navigation.ActivateSessionAsync(MixedConversationId, ProjectId).ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    $"The markdown conversation '{MixedConversationId}' did not activate.");
            }

            var firstPresenter = await WaitUntilAsync(
                () => FindRenderedPresenter(shellRoot), StepTimeoutMilliseconds).ConfigureAwait(true);
            loaded = firstPresenter is not null;
            App.BootLog($"MarkdownThemeProbe: step=loaded rendered={loaded}");

            if (loaded)
            {
                themeToggled = await ToggleThemeAsync().ConfigureAwait(true);
                App.BootLog($"MarkdownThemeProbe: step=theme toggled={themeToggled}");

                reactivated = ReactivateWindow();
                App.BootLog($"MarkdownThemeProbe: step=reactivate activated={reactivated}");

                // Switch the authoritative selection away: the plain sibling conversation has no
                // markdown rows, so the presenter must unload and dispose with its ThemeListener.
                if (!await navigation
                        .ActivateSessionAsync(PlainConversationId, ProjectId).ConfigureAwait(true))
                {
                    throw new InvalidOperationException(
                        $"The plain sibling conversation '{PlainConversationId}' did not activate.");
                }

                var gone = await WaitUntilAsync(
                    () => FindRenderedPresenter(shellRoot) is null, StepTimeoutMilliseconds).ConfigureAwait(true);
                unloaded = gone;
                App.BootLog($"MarkdownThemeProbe: step=unload gone={unloaded}");

                if (!await navigation
                        .ActivateSessionAsync(MixedConversationId, ProjectId).ConfigureAwait(true))
                {
                    throw new InvalidOperationException(
                        $"The markdown conversation '{MixedConversationId}' did not re-activate.");
                }

                var secondPresenter = await WaitUntilAsync(
                    () =>
                    {
                        var candidate = FindRenderedPresenter(shellRoot);
                        return candidate is not null && !ReferenceEquals(candidate, firstPresenter)
                            ? candidate
                            : null;
                    },
                    StepTimeoutMilliseconds).ConfigureAwait(true);
                reloaded = secondPresenter is not null;
                App.BootLog($"MarkdownThemeProbe: step=reload recreated={reloaded}");
            }

            var passed = loaded && themeToggled && reactivated && unloaded && reloaded;
            App.BootLog(
                $"MarkdownThemeProbe: complete loaded={loaded} themeToggled={themeToggled}"
                + $" reactivated={reactivated} unloaded={unloaded} reloaded={reloaded} passed={passed}");
        }
        catch (Exception ex)
        {
            // A dispatcher or layout fault anywhere in the lifecycle is the regression signature
            // the gate exists to catch; the absence of the complete marker fails the gate.
            App.BootLog($"MarkdownThemeProbe: faulted loaded={loaded} themeToggled={themeToggled}"
                + $" reactivated={reactivated} unloaded={unloaded} reloaded={reloaded} exception={ex}");
            throw;
        }
    }

    private static MarkdownTextPresenter? FindRenderedPresenter(DependencyObject root)
        => Find<MarkdownTextPresenter>(root, static presenter =>
            presenter.ActualHeight > 0 && presenter.Visibility == Visibility.Visible);

    private static async Task<bool> ToggleThemeAsync()
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement root)
        {
            return false;
        }

        var original = root.RequestedTheme;
        var opposite = original is ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
        root.RequestedTheme = opposite;
        await AwaitNextFrameAsync().ConfigureAwait(true);

        // The ThemeListener reacts on its own dispatcher subscription; give the theme change a
        // chance to propagate before restoring the preference.
        await Task.Delay(150).ConfigureAwait(true);
        var toggled = root.RequestedTheme == opposite;

        if (root.RequestedTheme != original)
        {
            root.RequestedTheme = original;
            await AwaitNextFrameAsync().ConfigureAwait(true);
        }

        return toggled;
    }

    private static bool ReactivateWindow()
    {
        if (App.MainWindowInstance is not { } window) return false;
        window.Activate();
        return true;
    }

    private static async Task AwaitNextFrameAsync()
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered(object? sender, object args) => rendered.TrySetResult();
        CompositionTarget.Rendering += OnRendered;
        try
        {
            await rendered.Task.ConfigureAwait(true);
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendered;
        }
    }

    private static async Task<T?> WaitUntilAsync<T>(Func<T?> probe, int timeoutMilliseconds)
        where T : class
    {
        using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
        while (true)
        {
            if (probe() is { } result) return result;
            await Task.Delay(PollDelayMilliseconds, cancellation.Token).ConfigureAwait(true);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
        while (!condition())
        {
            await Task.Delay(PollDelayMilliseconds, cancellation.Token).ConfigureAwait(true);
        }

        return true;
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : class, DependencyObject
    {
        predicate ??= static _ => true;
        if (root is T match && predicate(match)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (Find(VisualTreeHelper.GetChild(root, index), predicate) is { } descendant) return descendant;
        }

        return null;
    }
#endif
}

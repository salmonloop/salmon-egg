using System;
using Microsoft.UI.Xaml;

#if DEBUG && __UNO_SKIA__
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;
using SalmonEgg.Presentation.Views.Settings;
using Windows.Foundation;
#endif

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>
/// Exercises the real setup editor without assigning its text or restoring focus after a keystroke.
/// The external X11 gate supplies keyboard input; this opt-in driver only records native input facts.
/// </summary>
internal static class AcpSetupRuntimePathProbeDriver
{
    public static void TryStart(IServiceProvider services, DependencyObject shellRoot)
    {
#if DEBUG && __UNO_SKIA__
        if (Environment.GetEnvironmentVariable("SALMONEGG_ACP_SETUP_PATH_PROBE") != "1"
            || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AcpSetupRuntimePathProbe");
        _ = RunAsync(services.GetRequiredService<MainNavigationViewModel>(), shellRoot, logger);
#endif
    }

#if DEBUG && __UNO_SKIA__
    private static int _started;
    private const string FinalInput = "/tmp/acp-path-probe-next";

    // Keep navigation, event attachment and sampling in one scope so the same finally always
    // detaches the native input handler, including a failed assertion or a navigation timeout.
    private static async Task RunAsync(
        MainNavigationViewModel navigation,
        DependencyObject shellRoot,
        ILogger logger)
    {
        TextBox? input = null;
        TextChangedEventHandler? handler = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        try
        {
            if (!await navigation.ActivateSettingsAsync(SettingsSectionCatalog.AgentAcpKey).ConfigureAwait(true))
            {
                throw new InvalidOperationException("ACP settings navigation failed.");
            }

            var setup = await WaitForAsync(
                () => Find<Button>(shellRoot, element =>
                    AutomationProperties.GetAutomationId(element) == "Acp.Profiles.SetupWizard"), token).ConfigureAwait(true);
            Invoke(setup);
            var page = await WaitForAsync(
                () => Find<AcpSetupWizardPage>(shellRoot, element => element.IsLoaded), token).ConfigureAwait(true);
            var wizard = page.ViewModel;
            var agentId = Environment.GetEnvironmentVariable("SALMONEGG_ACP_SETUP_PATH_AGENT") ?? "qwen-code";
            var row = wizard.Agents.Single(candidate => candidate.AgentId == agentId);
            wizard.SelectedAgent = row;
            await wizard.GoNextCommand.ExecuteAsync(null).WaitAsync(token).ConfigureAwait(true);
            if (!row.IsMissing || !wizard.IsOnAgentSelection || wizard.IsBusy)
            {
                throw new InvalidOperationException("The real runtime probe did not establish a missing agent.");
            }

            logger.LogInformation(
                "AcpSetupPathProbe missing pid={ProcessId} agent={AgentId} command={Command} availability={Availability}",
                Environment.ProcessId, row.AgentId, row.ProbeCommand, row.Availability);
            var list = (ListView)page.FindName("AcpSetupAgentsList");
            list.ScrollIntoView(row);
            var container = await WaitForAsync(
                () => list.ContainerFromItem(row) as ListViewItem, token).ConfigureAwait(true);
            var expander = await WaitForAsync(
                () => Find<Expander>(container, element => element.Name == "AgentProbeDiagnosticsExpander"), token).ConfigureAwait(true);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(expander);
            if (peer?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expansion)
            {
                throw new InvalidOperationException("The native path disclosure has no expand provider.");
            }

            expansion.Expand();
            input = await WaitForAsync(
                () => Find<TextBox>(container, element =>
                    AutomationProperties.GetAutomationId(element) == "AcpSetup.Agents.CustomCommand"), token).ConfigureAwait(true);
            var verify = await WaitForAsync(
                () => Find<Button>(container, element =>
                    AutomationProperties.GetAutomationId(element) == "AcpSetup.Agents.VerifyCustomCommand"), token).ConfigureAwait(true);
            input.StartBringIntoView();
            await WaitForAsync(() => IsVisible(input) ? input : null, token).ConfigureAwait(true);
            if (!input.Focus(FocusState.Keyboard))
            {
                throw new InvalidOperationException("The path input did not accept initial keyboard focus.");
            }

            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var samples = 0;
            var targetInput = input;
            handler = (_, _) =>
            {
                // Binding and layout must finish before observing the edit's effects. This callback never
                // restores the input, selection, or focus, so hiding the editor remains a failing sample.
                _ = page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (finished.Task.IsCompleted)
                    {
                        return;
                    }

                    page.UpdateLayout();
                    var visible = IsVisible(targetInput);
                    var focusOwner = targetInput.XamlRoot is null
                        ? null
                        : FocusManager.GetFocusedElement(targetInput.XamlRoot);
                    var ownsFocus = ReferenceEquals(focusOwner, targetInput);
                    var bindingMatches = targetInput.Text == row.CustomCommand;
                    samples++;
                    logger.LogInformation(
                        "AcpSetupPathProbe sample={Sample} visible={Visible} focused={Focused} binding={Binding} verifyVisible={VerifyVisible} owner={Owner} text64={Text64}",
                        samples, visible, ownsFocus, bindingMatches, IsVisible(verify),
                        row.AgentId, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(targetInput.Text)));
                    if (!visible || !ownsFocus || !bindingMatches || !IsVisible(verify))
                    {
                        finished.TrySetException(new InvalidOperationException("The native path editor disappeared or lost its input owner."));
                    }
                    else if (targetInput.Text == FinalInput)
                    {
                        finished.TrySetResult();
                    }
                });
            };
            input.TextChanged += handler;
            var bounds = input.TransformToVisual(null).TransformBounds(new Rect(0, 0, input.ActualWidth, input.ActualHeight));
            logger.LogInformation(
                "AcpSetupPathProbe ready pid={ProcessId} agent={AgentId} x={X} y={Y} width={Width} height={Height} scale={Scale}",
                Environment.ProcessId, row.AgentId, bounds.X, bounds.Y, bounds.Width, bounds.Height, input.XamlRoot!.RasterizationScale);
            await finished.Task.WaitAsync(token).ConfigureAwait(true);
            logger.LogInformation("AcpSetupPathProbe complete passed={Passed} samples={Samples}", true, samples);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AcpSetupPathProbe complete passed={Passed}", false);
        }
        finally
        {
            if (input is not null && handler is not null)
            {
                input.TextChanged -= handler;
            }
        }
    }

    private static void Invoke(Button button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        if (!button.IsEnabled || peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invocation)
        {
            throw new InvalidOperationException("The native setup button cannot be invoked.");
        }

        invocation.Invoke();
    }

    private static bool IsVisible(FrameworkElement element)
    {
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 })
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> find, CancellationToken cancellationToken)
        where T : class
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (find() is { } value)
            {
                return value;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(true);
        }
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> matches)
        where T : class, DependencyObject
    {
        if (root is T candidate && matches(candidate))
        {
            return candidate;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (Find(VisualTreeHelper.GetChild(root, index), matches) is { } found)
            {
                return found;
            }
        }

        return null;
    }
#endif
}

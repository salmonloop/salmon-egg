using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Windows.Foundation;

namespace SalmonEgg.Presentation.Diagnostics;

internal static class NavigationInteractionProbeDriver
{
#if DEBUG
    public static async Task RunIfEnabledAsync(
        MainPage page,
        MainNavigationViewModel navigation,
        IChatStore chat,
        IReadOnlyList<SessionNavItemViewModel> sessions,
        CancellationToken token)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SALMONEGG_NAV_INTERACTION_PROBE"), "1", StringComparison.Ordinal)) return;
        var exchange = Environment.GetEnvironmentVariable("SALMONEGG_NAV_INTERACTION_EXCHANGE")
            ?? throw new InvalidOperationException("Interaction probe exchange directory is required.");
        var target = sessions[0];
        var otherWorking = sessions[1];
        var nativeNavigation = FindElements<NavigationView>(page).Single(view => view.Name == "MainNavView");
        await navigation.ActivateSessionAsync(target.SessionId, target.ProjectId).WaitAsync(token).ConfigureAwait(true);
        await chat.Dispatch(new BeginTurnAction(otherWorking.SessionId, "interaction-b", ChatTurnPhase.Thinking));
        await chat.Dispatch(new BeginTurnAction(target.SessionId, "interaction-a", ChatTurnPhase.Thinking));
        await WaitUntilAsync(() => Group(navigation, ConversationStatusGroup.Working).Count == 2, token).ConfigureAwait(true);
        await chat.Dispatch(new CompleteTurnAction(target.SessionId, "interaction-a", "end_turn", true));
        await WaitUntilAsync(() => Group(navigation, ConversationStatusGroup.Other).Children.Contains(target), token).ConfigureAwait(true);

        var workingContainer = FindGroupContainer(nativeNavigation, ConversationStatusGroup.Working);
        await EmitPointerTargetAsync(page, workingContainer, "ancestor", token).ConfigureAwait(true);
        await WaitForAcknowledgementAsync(exchange, "ancestor", token).ConfigureAwait(true);
        var indicators = GetVisibleIndicatorOwners(nativeNavigation);
        var ancestorPassed = !workingContainer.IsExpanded
            && navigation.CurrentSelection is NavigationSelectionState.Session selected && selected.SessionId == target.SessionId
            && indicators.SequenceEqual([NavItemTag.Session(target.SessionId)]);
        App.BootLog($"NavInteractionProbe result=ancestor passed={ancestorPassed} indicatorOwners=[{string.Join(",", indicators)}] selected={DescribeSelection(navigation)}");

        // Diagnostic setup uses the native expansion API; the next actions are real X11 key input.
        workingContainer.IsExpanded = true;
        FindGroupContainer(nativeNavigation, ConversationStatusGroup.Other).IsExpanded = false;
        await chat.Dispatch(new BeginTurnAction(target.SessionId, "interaction-keyboard", ChatTurnPhase.Thinking));
        await WaitUntilAsync(() => Group(navigation, ConversationStatusGroup.Working).Children.Contains(target), token).ConfigureAwait(true);
        await navigation.ActivateSessionAsync(target.SessionId, target.ProjectId).WaitAsync(token).ConfigureAwait(true);
        var targetContainer = await WaitForSessionContainerAsync(nativeNavigation, target.SessionId, token).ConfigureAwait(true);
        await EmitPointerTargetAsync(page, targetContainer, "keyboard", token).ConfigureAwait(true);
        await WaitForAcknowledgementAsync(exchange, "keyboard", token).ConfigureAwait(true);
        var focusBefore = FocusedNavigationTag(page);
        await chat.Dispatch(new CompleteTurnAction(target.SessionId, "interaction-keyboard", "end_turn", true));
        await WaitUntilAsync(() => target.StatusIcon == ConversationStatusIcon.Conversation, token).ConfigureAwait(true);
        var heldInSource = Group(navigation, ConversationStatusGroup.Working).Children.Contains(target);
        App.BootLog($"NavInteractionProbe ready=enter focusBefore={focusBefore} focusAfter={FocusedNavigationTag(page)} heldInSource={heldInSource}");
        await WaitForAcknowledgementAsync(exchange, "enter", token).ConfigureAwait(true);
        var keyboardPassed = heldInSource && focusBefore == NavItemTag.Session(target.SessionId)
            && FocusedNavigationTag(page) == NavItemTag.Session(target.SessionId)
            && navigation.CurrentSelection is NavigationSelectionState.Session finalSelection && finalSelection.SessionId == target.SessionId;
        App.BootLog($"NavInteractionProbe result=keyboard passed={keyboardPassed} selected={DescribeSelection(navigation)} focus={FocusedNavigationTag(page)}");
        await EmitPointerTargetAsync(page, FindGroupContainer(nativeNavigation, ConversationStatusGroup.NeedsAttention), "leave-focus", token).ConfigureAwait(true);
        await WaitForAcknowledgementAsync(exchange, "leave-focus", token).ConfigureAwait(true);
        await WaitUntilAsync(() => Group(navigation, ConversationStatusGroup.Other).Children.Contains(target), token).ConfigureAwait(true);
        var released = !Group(navigation, ConversationStatusGroup.Working).Children.Contains(target)
            && navigation.CurrentSelection is NavigationSelectionState.Session releasedSelection && releasedSelection.SessionId == target.SessionId;
        App.BootLog($"NavInteractionProbe result=focus-release passed={released} selected={DescribeSelection(navigation)} focus={FocusedNavigationTag(page)}");
        App.BootLog($"NavInteractionProbe complete passed={ancestorPassed && keyboardPassed && released}");
    }

    private static StatusGroupNavItemViewModel Group(MainNavigationViewModel navigation, ConversationStatusGroup group)
        => navigation.Items.OfType<StatusGroupNavItemViewModel>().Single(item => item.Group == group);

    private static NavigationViewItem FindGroupContainer(NavigationView navigation, ConversationStatusGroup group)
        => FindElements<NavigationViewItem>(navigation).Single(item => item.Tag as string == NavItemTag.StatusGroup(group));

    private static async Task<NavigationViewItem> WaitForSessionContainerAsync(NavigationView navigation, string id, CancellationToken token)
    {
        NavigationViewItem? result = null;
        await WaitUntilAsync(() => (result = FindElements<NavigationViewItem>(navigation)
            .FirstOrDefault(item => item.Tag as string == NavItemTag.Session(id) && item.ActualHeight > 0)) is not null, token).ConfigureAwait(true);
        return result!;
    }

    private static async Task EmitPointerTargetAsync(MainPage page, NavigationViewItem container, string phase, CancellationToken token)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered(object? sender, object args) => rendered.TrySetResult();
        CompositionTarget.Rendering += OnRendered;
        try
        {
            await rendered.Task.WaitAsync(token).ConfigureAwait(true);
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendered;
        }

        var content = container.Content as FrameworkElement
            ?? throw new InvalidOperationException("The native navigation row has no measurable content.");
        var position = content.TransformToVisual(page).TransformPoint(new Point(content.ActualWidth / 2, content.ActualHeight / 2));
        App.BootLog(FormattableString.Invariant($"NavInteractionProbe ready={phase} x={position.X:0} y={position.Y:0}"));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, deadline.Token).ConfigureAwait(true);
    }

    private static Task WaitForAcknowledgementAsync(string directory, string phase, CancellationToken token)
        => WaitUntilAsync(() => File.Exists(Path.Combine(directory, phase + ".done")), token);

    private static string DescribeSelection(MainNavigationViewModel navigation)
        => navigation.CurrentSelection is NavigationSelectionState.Session selection ? selection.SessionId : "none";

    private static string FocusedNavigationTag(MainPage page)
    {
        if (page.XamlRoot is not { } root) return "none";
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is NavigationViewItem item) return item.Tag as string ?? "none";
        }
        return "none";
    }

    private static string[] GetVisibleIndicatorOwners(NavigationView navigation)
        => FindElements<NavigationViewItem>(navigation)
            .Where(container => OwnIndicators(container).Any(IndicatorVisible))
            .Select(container => container.Tag as string ?? "none").ToArray();

    private static IEnumerable<FrameworkElement> OwnIndicators(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is NavigationViewItem) continue;
            if (child is FrameworkElement { Name: "SelectionIndicator" } indicator) yield return indicator;
            foreach (var descendant in OwnIndicators(child)) yield return descendant;
        }
    }

    private static bool IndicatorVisible(FrameworkElement indicator)
    {
        if (indicator.ActualWidth <= 0 || indicator.ActualHeight <= 0) return false;
        var opacity = 1.0;
        for (var element = (DependencyObject)indicator; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not FrameworkElement visual) continue;
            if (visual.Visibility != Visibility.Visible) return false;
            opacity *= visual.Opacity * ElementCompositionPreview.GetElementVisual(visual).Opacity;
        }
        return opacity > 0.5;
    }

    private static IEnumerable<T> FindElements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in FindElements<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
#endif
}

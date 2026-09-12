using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Diagnostics;

/// <summary>
/// Diagnostics-only stress driver for the left navigation pane's selection visual.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the conditions under which Uno's NavigationView strands its selected mask on more
/// than one row: rapid session switching while conversation-catalog ticks keep reordering the bound
/// children. Reordering a rendered row makes <c>ItemsRepeater</c> recycle its realized container
/// (Move is decomposed into Remove+Add), the recycle pool keeps the selected flag, and the deselect
/// of the previous row no-ops once its container is gone.
/// </para>
/// <para>
/// The driver only produces that load. Measurement belongs to the view, which is the only layer
/// that can see realized containers; it audits them on every navigation-state change and writes the
/// selected-container count to boot.log for <c>scripts/gates/run-skia-nav-mask-probe.sh</c> to
/// assert on.
/// </para>
/// <para>
/// Compiled out of release builds and inert unless <c>SALMONEGG_NAV_MASK_PROBE=1</c>.
/// </para>
/// </remarks>
internal static class NavigationMaskProbeDriver
{
    private const string EnableVariable = "SALMONEGG_NAV_MASK_PROBE";
    private const int Rounds = 60;
    private const int ActivationIntervalMilliseconds = 60;
    private const int CatalogChurnIntervalMilliseconds = 5;
    private const int TreeSettleDelayMilliseconds = 1500;
    private const int QuiesceDelayMilliseconds = 800;
#if DEBUG
    private static int _started;
#endif

    /// <summary>
    /// Starts the stress run when explicitly enabled; otherwise does nothing.
    /// </summary>
    public static void TryStart(IServiceProvider services, MainPage page)
    {
        ArgumentNullException.ThrowIfNull(services);

#if DEBUG
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var navigation = services.GetRequiredService<MainNavigationViewModel>();
        if (navigation.IsStatusGrouping)
        {
            _ = RunStatusAsync(
                navigation,
                services.GetRequiredService<IChatStore>(),
                services.GetRequiredService<IConversationAttentionStore>(),
                services.GetRequiredService<IApplicationShutdownProgress>(),
                services.GetRequiredService<IUiDispatcher>(),
                services.GetRequiredService<IShellNavigationRuntimeState>(),
                services.GetRequiredService<IConversationCatalogDisplayReadModel>(),
                page);
            return;
        }

        var catalog = services.GetService<ConversationCatalogPresenter>();
        if (catalog is null)
        {
            App.BootLog("NavMaskProbe: catalog presenter unavailable; driver not started");
            return;
        }

        _ = RunAsync(navigation, catalog);
#endif
    }

#if DEBUG
    private static async Task RunStatusAsync(
        MainNavigationViewModel navigation,
        IChatStore chat,
        IConversationAttentionStore attention,
        IApplicationShutdownProgress shutdown,
        IUiDispatcher dispatcher,
        IShellNavigationRuntimeState runtime,
        IConversationCatalogDisplayReadModel display,
        MainPage page)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        void OnShutdown(object? sender, PropertyChangedEventArgs args)
        {
            if (shutdown.IsShuttingDown) stop.Cancel();
        }

        shutdown.PropertyChanged += OnShutdown;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (navigation.Items.SelectMany(group => group.Children).OfType<SessionNavItemViewModel>()
                       .Count(row => !row.IsPlaceholder) < 3)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Fixture navigation never materialized three sessions.");
                await Task.Delay(50, stop.Token).ConfigureAwait(true);
            }

            var sessions = navigation.Items.SelectMany(group => group.Children)
                .OfType<SessionNavItemViewModel>().Where(row => !row.IsPlaceholder)
                .OrderBy(row => row.SessionId, StringComparer.Ordinal).Take(3).ToArray();
            if (sessions.Length != 3)
            {
                throw new InvalidOperationException("The status probe requires three production conversation rows.");
            }

            App.BootLog("NavMaskProbe: status run started");
            var step = 0;
            for (var round = 0; round < 3; round++)
            {
                foreach (var row in sessions)
                {
                    if (!await navigation.ActivateSessionAsync(row.SessionId, row.ProjectId).WaitAsync(stop.Token).ConfigureAwait(true))
                    {
                        throw new InvalidOperationException($"Could not activate fixture conversation {row.SessionId}.");
                    }

                    App.BootLog($"NavStatusProbeActivation conversationId={row.SessionId} activePhase={runtime.ActiveSessionActivation?.Phase} inProgress={runtime.IsSessionActivationInProgress} activationVersion={runtime.ActiveSessionActivation?.Version}");
                    var turnId = $"nav-status-probe-{round}-{row.SessionId}";
                    await ApplyStatusStepAsync(navigation, row, ConversationStatusGroup.Working, ++step, sessions.Length,
                        () => chat.Dispatch(new BeginTurnAction(row.SessionId, turnId, ChatTurnPhase.Thinking)), dispatcher, stop.Token, chat, runtime, display).ConfigureAwait(true);
                    await ApplyStatusStepAsync(navigation, row, ConversationStatusGroup.NeedsAttention, ++step, sessions.Length,
                        async () =>
                        {
                            await attention.Dispatch(new MarkConversationUnreadAction(row.SessionId, ConversationAttentionSource.AgentMessage, DateTime.UtcNow));
                            await chat.Dispatch(new CompleteTurnAction(row.SessionId, turnId, "end_turn", HasStopReason: true));
                        }, dispatcher, stop.Token, chat, runtime, display).ConfigureAwait(true);
                    await ApplyStatusStepAsync(navigation, row, ConversationStatusGroup.Other, ++step, sessions.Length,
                        async () =>
                        {
                            var state = await attention.GetCurrentStateAsync();
                            if (state.TryGetConversation(row.SessionId, out var pending) && pending is not null)
                            {
                                await attention.Dispatch(new ClearConversationUnreadAction(row.SessionId, pending.UnreadVersion,
                                    pending.ProfileId, pending.RemoteSessionId, pending.Content, pending.ContentConnectionInstanceId));
                            }
                        }, dispatcher, stop.Token, chat, runtime, display).ConfigureAwait(true);
                }
            }

            App.BootLog($"NavMaskProbe: status run complete steps={step}");
            await NavigationInteractionProbeDriver.RunIfEnabledAsync(page, navigation, chat, sessions, stop.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            App.BootLog($"NavMaskProbe: status run faulted {ex}");
        }
        finally
        {
            shutdown.PropertyChanged -= OnShutdown;
        }
    }

    private static async Task ApplyStatusStepAsync(
        MainNavigationViewModel navigation,
        SessionNavItemViewModel row,
        ConversationStatusGroup expectedGroup,
        int step,
        int sessionCount,
        Func<ValueTask> apply,
        IUiDispatcher dispatcher,
        CancellationToken token,
        IChatStore chat,
        IShellNavigationRuntimeState runtime,
        IConversationCatalogDisplayReadModel display)
    {
        var projected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(object? sender, EventArgs args)
        {
            var group = navigation.Items.OfType<StatusGroupNavItemViewModel>()
                .SingleOrDefault(item => item.Group == expectedGroup);
            var published = display.Snapshot.FirstOrDefault(item => item.ConversationId == row.SessionId);
            var applied = navigation.Items.OfType<StatusGroupNavItemViewModel>().FirstOrDefault(item => item.Children.Contains(row));
            App.BootLog($"NavStatusProbeRebuilt step={step} conversationId={row.SessionId} publishedGroup={published?.StatusGroup} appliedGroup={applied?.Group} activePhase={runtime.ActiveSessionActivation?.Phase} inProgress={runtime.IsSessionActivationInProgress}");
            if (group?.Children.Contains(row) == true) projected.TrySetResult();
        }

        navigation.TreeRebuilt += Observe;
        try
        {
            await apply().ConfigureAwait(false);
            var state = await chat.GetCurrentStateAsync().ConfigureAwait(false);
            App.BootLog($"NavStatusProbeStore step={step} conversationId={row.SessionId} turnPhase={state.ResolveTurn(row.SessionId)?.Phase}");
            await projected.Task.WaitAsync(token).ConfigureAwait(false);
            await dispatcher.EnqueueAsync(() =>
            {
                var groups = navigation.Items.OfType<StatusGroupNavItemViewModel>().ToArray();
                var rows = groups.SelectMany(group => group.Children).OfType<SessionNavItemViewModel>()
                    .Where(item => !item.IsPlaceholder).ToArray();
                var actual = groups.Single(group => group.Children.Contains(row)).Group;
                var stable = ReferenceEquals(navigation.ProjectedControlSelectedItem, row)
                    && navigation.CurrentSelection is NavigationSelectionState.Session selection && selection.SessionId == row.SessionId;
                var unique = rows.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count();
                var total = groups.Sum(group => group.Count);
                App.BootLog($"NavStatusProbe step={step} conversationId={row.SessionId} expectedGroup={expectedGroup} actualGroup={actual} countTotal={total} unique={unique} selectedStable={stable}");
                if (!stable || actual != expectedGroup || total != sessionCount || unique != sessionCount)
                {
                    throw new InvalidOperationException("Status navigation projection violated the fixture contract.");
                }
            }).ConfigureAwait(false);
            // Bounded diagnostic pacing permits actual native frames between distinct status steps;
            // it is not used to decide whether the semantic projection succeeded.
            await Task.Delay(80, token).ConfigureAwait(false);
        }
        finally
        {
            navigation.TreeRebuilt -= Observe;
        }
    }

    private static async Task RunAsync(MainNavigationViewModel navigation, ConversationCatalogPresenter catalog)
    {
        try
        {
            // Let the first navigation settle so the pane has realized containers to stress.
            await Task.Delay(TreeSettleDelayMilliseconds).ConfigureAwait(true);

            using var churnStop = new CancellationTokenSource();
            var churn = Task.Run(() => PumpCatalogChurnAsync(catalog, churnStop.Token));

            App.BootLog("NavMaskProbe: stress run started");
            for (var round = 0; round < Rounds; round++)
            {
                var sessions = navigation.Items
                    .OfType<ProjectNavItemViewModel>()
                    .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
                    .Where(session => !session.IsPlaceholder && !string.IsNullOrWhiteSpace(session.SessionId))
                    .ToArray();

                if (sessions.Length < 2)
                {
                    App.BootLog($"NavMaskProbe: only {sessions.Length} session(s) realized; stress run aborted");
                    break;
                }

                // Stride through the set so consecutive rounds rarely touch adjacent rows.
                var next = sessions[(round * 7 + 3) % sessions.Length];
                _ = navigation.ActivateSessionAsync(next.SessionId, next.ProjectId);

                await Task.Delay(ActivationIntervalMilliseconds).ConfigureAwait(true);
            }

            churnStop.Cancel();
            await churn.ConfigureAwait(true);

            // Give the pane time to quiesce so the audit also covers the settled state.
            await Task.Delay(QuiesceDelayMilliseconds).ConfigureAwait(true);
            App.BootLog("NavMaskProbe: stress run complete");
        }
        catch (Exception ex)
        {
            App.BootLog($"NavMaskProbe: stress run faulted {ex}");
        }
    }

    private static async Task PumpCatalogChurnAsync(
        ConversationCatalogPresenter catalog,
        CancellationToken cancellationToken)
    {
        var round = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = catalog.Snapshot;
                if (snapshot.Count > 1)
                {
                    catalog.Refresh(BuildRotatedSnapshot(snapshot, round));
                    round++;
                }
            }
            catch (Exception ex)
            {
                App.BootLog($"NavMaskProbe: catalog churn tick faulted {ex}");
            }

            try
            {
                await Task.Delay(CatalogChurnIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Rotates recency so a different row wants to lead each tick, which is what drives the pane to
    /// reorder its bound children while a selection is in flight.
    /// </summary>
    private static List<ConversationCatalogItem> BuildRotatedSnapshot(
        IReadOnlyList<ConversationCatalogItem> snapshot,
        int round)
    {
        var rotateBy = round % snapshot.Count;
        var rotated = new List<ConversationCatalogItem>(snapshot.Count);
        for (var index = 0; index < snapshot.Count; index++)
        {
            var source = snapshot[(index + rotateBy) % snapshot.Count];
            rotated.Add(source with { CatalogUpdatedAt = DateTime.UtcNow.AddSeconds(round + index) });
        }

        return rotated;
    }
#endif
}

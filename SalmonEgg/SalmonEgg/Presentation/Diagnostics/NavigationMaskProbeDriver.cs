using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SalmonEgg.Presentation.Core.Services.Chat;
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

    /// <summary>
    /// Starts the stress run when explicitly enabled; otherwise does nothing.
    /// </summary>
    public static void TryStart(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

#if DEBUG
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var navigation = services.GetRequiredService<MainNavigationViewModel>();
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

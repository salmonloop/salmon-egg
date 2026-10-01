using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record AcpConnectionDependencySnapshot(
    string? SelectedProfileId,
    IImmutableSet<string> ProfilesRequiredByRemoteBindings)
{
    public IImmutableSet<(string ProfileId, string ConnectionInstanceId)> BusyConnections { get; init; }
        = ImmutableHashSet<(string, string)>.Empty;

    public static AcpConnectionDependencySnapshot Empty { get; } = new(
        SelectedProfileId: null,
        ProfilesRequiredByRemoteBindings: ImmutableHashSet.Create<string>(StringComparer.Ordinal));
}

public interface IAcpConnectionDependencySnapshotProvider
{
    ValueTask<AcpConnectionDependencySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionDependencySnapshotProvider : IAcpConnectionDependencySnapshotProvider
{
    private readonly IChatStore _chatStore;
    private readonly IChatConnectionStore _chatConnectionStore;
    private readonly ChatConversationPanelStateCoordinator? _panelStateCoordinator;
    private readonly IAcpConnectionSessionRegistry? _sessionRegistry;

    public AcpConnectionDependencySnapshotProvider(
        IChatStore chatStore,
        IChatConnectionStore chatConnectionStore,
        ChatConversationPanelStateCoordinator? panelStateCoordinator = null,
        IAcpConnectionSessionRegistry? sessionRegistry = null)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
        _panelStateCoordinator = panelStateCoordinator;
        _sessionRegistry = sessionRegistry;
    }

    public async ValueTask<AcpConnectionDependencySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chatState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        var profiles = (chatState.Bindings ?? ImmutableDictionary<string, ConversationBindingSlice>.Empty)
            .Values
            .Where(binding =>
                !string.IsNullOrWhiteSpace(binding.RemoteSessionId)
                && !string.IsNullOrWhiteSpace(binding.ProfileId))
            .Select(binding => binding.ProfileId!)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var busyConnections = ImmutableHashSet.CreateBuilder<(string ProfileId, string ConnectionInstanceId)>();
        foreach (var turn in chatState.Turns?.Values ?? Enumerable.Empty<ActiveTurnState>())
        {
            if (turn.Phase is not (ChatTurnPhase.Completed or ChatTurnPhase.Failed or ChatTurnPhase.Cancelled)
                && turn.ProfileId is { Length: > 0 } profileId
                && turn.ConnectionInstanceId is { Length: > 0 } connectionInstanceId)
            {
                busyConnections.Add((profileId, connectionInstanceId));
            }
        }

        if (_panelStateCoordinator is not null)
        {
            foreach (var source in await _panelStateCoordinator.GetPendingConnectionSourcesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (source.ProfileId is { Length: > 0 } profileId
                    && source.ConnectionInstanceId is { Length: > 0 } connectionInstanceId
                    && (_sessionRegistry is null
                        || _sessionRegistry.TryGetByProfile(profileId, out var session) && source.Matches(session)))
                {
                    busyConnections.Add((profileId, connectionInstanceId));
                }
            }
        }

        return new AcpConnectionDependencySnapshot(connectionState.ForegroundTransportProfileId, profiles)
        {
            BusyConnections = busyConnections.ToImmutable()
        };
    }
}

public sealed class NoopAcpConnectionDependencySnapshotProvider : IAcpConnectionDependencySnapshotProvider
{
    public static NoopAcpConnectionDependencySnapshotProvider Instance { get; } = new();

    private NoopAcpConnectionDependencySnapshotProvider()
    {
    }

    public ValueTask<AcpConnectionDependencySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AcpConnectionDependencySnapshot.Empty);
    }
}

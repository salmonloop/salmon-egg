using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record AcpConnectionDependencySnapshot(
    string? SelectedProfileId,
    IImmutableSet<string> ProfilesRequiredByRemoteBindings)
{
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

    public AcpConnectionDependencySnapshotProvider(
        IChatStore chatStore,
        IChatConnectionStore chatConnectionStore)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
    }

    public async ValueTask<AcpConnectionDependencySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chatState = await _chatStore.State ?? ChatState.Empty;
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        var profiles = (chatState.Bindings ?? ImmutableDictionary<string, ConversationBindingSlice>.Empty)
            .Values
            .Where(binding =>
                !string.IsNullOrWhiteSpace(binding.RemoteSessionId)
                && !string.IsNullOrWhiteSpace(binding.ProfileId))
            .Select(binding => binding.ProfileId!)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return new AcpConnectionDependencySnapshot(connectionState.SelectedProfileId, profiles);
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

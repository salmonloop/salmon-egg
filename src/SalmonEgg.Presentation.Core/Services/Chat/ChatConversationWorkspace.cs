using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatConversationWorkspace : ObservableObject, IConversationCatalog, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
    private readonly IConversationWorkspacePreferences _preferences;
    private readonly ILogger<ChatConversationWorkspace> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, ConversationBinding> _conversationBindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedConversationTombstones = new(StringComparer.Ordinal);
    private CancellationTokenSource? _saveCts;
    private bool _disposed;
    private bool _recoveryDocumentRestored;
    private bool _isConversationListLoading = true;
    private int _conversationListVersion;
    private string? _lastActiveConversationId;

    public ChatConversationWorkspace(
        ISessionManager sessionManager,
        IConversationStore conversationStore,
        IConversationWorkspacePreferences preferences,
        ILogger<ChatConversationWorkspace> logger,
        IUiDispatcher uiDispatcher)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public bool IsConversationListLoading
    {
        get => _isConversationListLoading;
        private set => SetProperty(ref _isConversationListLoading, value);
    }

    public int ConversationListVersion
    {
        get => _conversationListVersion;
        private set => SetProperty(ref _conversationListVersion, value);
    }

    public string? LastActiveConversationId
    {
        get => _lastActiveConversationId;
        private set => SetProperty(ref _lastActiveConversationId, value);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await PostToContextAsync(() => IsConversationListLoading = true, cancellationToken).ConfigureAwait(false);

        ConversationDocument document;
        try
        {
            document = await _conversationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore conversation workspace; persistence stays disabled to protect the stored history");
            await PostToContextAsync(() => IsConversationListLoading = false, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var conversation in document.Conversations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(conversation.ConversationId))
            {
                continue;
            }

            var restoredCwd = ResolveConversationRecordCwd(conversation);
            var existingSession = _sessionManager.GetSession(conversation.ConversationId);
            if (existingSession is not null)
            {
                if (!string.IsNullOrWhiteSpace(restoredCwd)
                    && string.IsNullOrWhiteSpace(existingSession.Cwd))
                {
                    _sessionManager.UpdateSession(
                        conversation.ConversationId,
                        session => session.Cwd = restoredCwd,
                        updateActivity: false);
                }

                continue;
            }

            try
            {
                await _sessionManager.CreateSessionAsync(conversation.ConversationId, restoredCwd).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to create missing session during workspace restore (ConversationId={ConversationId})", conversation.ConversationId);
            }
        }

        await PostToContextAsync(() =>
        {
            try
            {
                ApplyRestoredDocument(document);
            }
            finally
            {
                IsConversationListLoading = false;
                NotifyConversationListChanged();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public string[] GetKnownConversationIds()
    {
        lock (_stateGate)
        {
            return _conversationBindings.Values
                .OrderByDescending(ResolveCatalogUpdatedAt)
                .ThenByDescending(binding => binding.LastUpdatedAt)
                .Select(binding => binding.ConversationId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }
    }

    public IReadOnlyList<ConversationCatalogItem> GetCatalog()
    {
        lock (_stateGate)
        {
            return _conversationBindings.Values
                .OrderByDescending(ResolveCatalogUpdatedAt)
                .ThenByDescending(binding => binding.LastUpdatedAt)
                .Select(binding => new ConversationCatalogItem(
                    binding.ConversationId,
                    ResolveSessionDisplayName(binding.ConversationId),
                    ResolveEstablishedConversationCwd(binding),
                    binding.CreatedAt,
                    ResolveCatalogUpdatedAt(binding),
                    binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt,
                    binding.RemoteSessionId,
                    binding.BoundProfileId,
                    binding.ProjectAffinityOverride?.ProjectId))
                .ToArray();
        }
    }

    public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        => RemoveConversationTransactionallyAsync(conversationId, cancellationToken);

    public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        => RemoveConversationTransactionallyAsync(conversationId, cancellationToken);

    private async Task<ConversationMutationResult> RemoveConversationTransactionallyAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new ConversationMutationResult(true, false, null);
        }

        // Stage workspace-owned state only. Leave ISessionManager intact until the
        // recovery document is persisted successfully so a failed save never needs
        // to reconstruct a partial Session pre-image.
        var staged = TryStageConversationRemoval(conversationId);
        if (staged is null)
        {
            return new ConversationMutationResult(true, false, null);
        }

        try
        {
            await PersistRecoveryMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RestoreStagedConversationRemoval(staged);
            throw;
        }
        catch (Exception ex)
        {
            RestoreStagedConversationRemoval(staged);
            _logger.LogWarning(
                ex,
                "Conversation removal persistence failed; workspace rolled back. ConversationId={ConversationId}",
                conversationId);
            return new ConversationMutationResult(false, false, "ConversationRemovalPersistFailed");
        }

        _sessionManager.RemoveSession(conversationId);
        return new ConversationMutationResult(true, false, null);
    }

    private StagedConversationRemoval? TryStageConversationRemoval(string conversationId)
    {
        StagedConversationRemoval? staged;
        lock (_stateGate)
        {
            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return null;
            }

            var previousLastActiveConversationId = LastActiveConversationId;
            var wasTombstoned = _deletedConversationTombstones.Contains(conversationId);

            if (string.Equals(LastActiveConversationId, conversationId, StringComparison.Ordinal))
            {
                LastActiveConversationId = null;
            }

            _conversationBindings.Remove(conversationId);
            _deletedConversationTombstones.Add(conversationId);

            staged = new StagedConversationRemoval(binding, wasTombstoned, previousLastActiveConversationId);
        }

        NotifyConversationListChanged();
        return staged;
    }

    private void RestoreStagedConversationRemoval(StagedConversationRemoval staged)
    {
        lock (_stateGate)
        {
            _conversationBindings[staged.Binding.ConversationId] = staged.Binding;
            if (!staged.WasTombstoned)
            {
                _deletedConversationTombstones.Remove(staged.Binding.ConversationId);
            }

            LastActiveConversationId = staged.PreviousLastActiveConversationId;
        }

        NotifyConversationListChanged();
    }

    private sealed record StagedConversationRemoval(
        ConversationBinding Binding,
        bool WasTombstoned,
        string? PreviousLastActiveConversationId);

    public Task<bool> TryPrepareConversationActivationAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public async Task<bool> CommitActivatedConversationAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (string.Equals(LastActiveConversationId, sessionId, StringComparison.Ordinal))
        {
            return true;
        }

        await _sessionSwitchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PostToContextAsync(() =>
            {
                LastActiveConversationId = sessionId;
                if (UpdateLastAccessedAt(sessionId, DateTime.UtcNow))
                {
                    ScheduleSave();
                }
            }, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Committing workspace conversation activation failed (ConversationId={ConversationId})", sessionId);
            return false;
        }
        finally
        {
            _sessionSwitchGate.Release();
        }
    }

    public ConversationWorkspaceSnapshot? GetConversationSnapshot(string? conversationId)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return null;
            }

            return new ConversationWorkspaceSnapshot(
                binding.ConversationId,
                CloneMessages(binding.Transcript).ToArray(),
                binding.Plan.Select(ClonePlanEntry).ToArray(),
                binding.ShowPlanPanel,
                binding.CreatedAt,
                binding.LastUpdatedAt,
                binding.AvailableModes.Select(CloneModeOption).ToArray(),
                binding.SelectedModeId,
                binding.ConfigOptions.Select(CloneConfigOption).ToArray(),
                binding.ShowConfigOptionsPanel,
                binding.AvailableCommands.Select(CloneAvailableCommand).ToArray(),
                ConversationSessionInfoSnapshots.Clone(binding.SessionInfo),
                CloneUsage(binding.Usage),
                binding.SnapshotConnectionInstanceId);
        }
    }

    public ConversationRemoteBindingState? GetRemoteBinding(string? conversationId)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return null;
            }

            return new ConversationRemoteBindingState(binding.ConversationId, binding.RemoteSessionId, binding.BoundProfileId);
        }
    }

    public ConversationWorkspaceSnapshotOrigin? GetConversationSnapshotOrigin(string? conversationId)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return null;
            }

            return binding.SnapshotOrigin;
        }
    }

    public ProjectAffinityOverride? GetProjectAffinityOverride(string? conversationId)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return null;
            }

            return binding.ProjectAffinityOverride;
        }
    }

    public void UpdateProjectAffinityOverride(string conversationId, string? projectId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var conversationListChanged = false;
        var shouldSave = false;
        lock (_stateGate)
        {
            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                binding = RegisterConversationCore(
                    conversationId,
                    default,
                    DateTime.UtcNow,
                    bumpVersion: true,
                    clearTombstone: false,
                    out var registeredConversationListChanged);
                conversationListChanged |= registeredConversationListChanged;
            }

            var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
            var newOverride = normalizedProjectId is null ? null : new ProjectAffinityOverride(normalizedProjectId);
            if (Equals(binding.ProjectAffinityOverride, newOverride))
            {
                return;
            }

            binding.ProjectAffinityOverride = newOverride;
            binding.LastUpdatedAt = DateTime.UtcNow;
            conversationListChanged = true;
            shouldSave = true;
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }

        if (shouldSave)
        {
            ScheduleSave();
        }
    }

    public void UpsertConversationSnapshot(
        ConversationWorkspaceSnapshot snapshot,
        ConversationWorkspaceSnapshotOrigin origin = ConversationWorkspaceSnapshotOrigin.Restored)
    {
        ThrowIfDisposed();
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.ConversationId))
        {
            return;
        }

        var conversationListChanged = false;
        lock (_stateGate)
        {
            if (!_conversationBindings.ContainsKey(snapshot.ConversationId)
                && _deletedConversationTombstones.Contains(snapshot.ConversationId))
            {
                _logger.LogDebug(
                    "Ignore snapshot upsert for deleted conversation. ConversationId={ConversationId}",
                    snapshot.ConversationId);
                return;
            }

            var binding = RegisterConversationCore(
                snapshot.ConversationId,
                snapshot.CreatedAt,
                snapshot.LastUpdatedAt,
                bumpVersion: true,
                clearTombstone: false,
                out var registeredConversationListChanged);
            conversationListChanged |= registeredConversationListChanged;
            var canApplyRuntimeContent = CanApplySnapshotRuntimeContent(binding, origin);
            var preserveAuthoritativeRuntimeProjection = ShouldPreserveAuthoritativeRuntimeProjection(
                binding,
                canApplyRuntimeContent);
            if (!preserveAuthoritativeRuntimeProjection)
            {
                ClearRuntimeContentCore(binding, preserveSessionInfo: true);
            }

            if (canApplyRuntimeContent)
            {
                ApplySnapshotRuntimeContentCore(binding, snapshot);
            }

            var mergedSessionInfo = snapshot.SessionInfo is null
                ? ConversationSessionInfoSnapshots.Clone(binding.SessionInfo)
                : ConversationSessionInfoSnapshots.Merge(binding.SessionInfo, snapshot.SessionInfo);
            binding.SessionInfo = EnsureSessionInfoCarriesEstablishedCwd(
                mergedSessionInfo,
                ResolveEstablishedConversationCwd(binding));
            if (!preserveAuthoritativeRuntimeProjection)
            {
                ApplySnapshotIdentityCore(binding, snapshot, origin);
            }
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }
    }

    public void UpdateRemoteBinding(string conversationId, string? remoteSessionId, string? boundProfileId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var conversationListChanged = false;
        lock (_stateGate)
        {
            if (!_conversationBindings.ContainsKey(conversationId)
                && _deletedConversationTombstones.Contains(conversationId))
            {
                _logger.LogDebug(
                    "Ignore remote binding update for deleted conversation. ConversationId={ConversationId}",
                    conversationId);
                return;
            }

            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                binding = RegisterConversationCore(
                    conversationId,
                    default,
                    DateTime.UtcNow,
                    bumpVersion: true,
                    clearTombstone: false,
                    out var registeredConversationListChanged);
                conversationListChanged |= registeredConversationListChanged;
            }

            binding.RemoteSessionId = remoteSessionId;
            binding.BoundProfileId = boundProfileId;
            if (RemoteConversationPersistencePolicy.IsRemoteBacked(remoteSessionId, boundProfileId)
                && binding.SnapshotOrigin is not ConversationWorkspaceSnapshotOrigin.RuntimeProjection)
            {
                ClearRuntimeContentCore(binding, preserveSessionInfo: true);
            }
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }
    }

    public void ClearConversationRuntimeContent(string conversationId, bool preserveSessionInfo = true)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        lock (_stateGate)
        {
            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return;
            }

            ClearRuntimeContentCore(binding, preserveSessionInfo);
        }
    }

    private static void ClearRuntimeContentCore(ConversationBinding binding, bool preserveSessionInfo)
    {
        binding.Transcript.Clear();
        binding.Plan.Clear();
        binding.AvailableModes.Clear();
        binding.SelectedModeId = null;
        binding.ConfigOptions.Clear();
        binding.ShowConfigOptionsPanel = false;
        binding.AvailableCommands.Clear();
        if (!preserveSessionInfo)
        {
            binding.SessionInfo = null;
        }

        binding.Usage = null;
        binding.ShowPlanPanel = false;
        binding.SnapshotOrigin = ConversationWorkspaceSnapshotOrigin.Restored;
        binding.SnapshotConnectionInstanceId = null;
    }

    private static bool CanApplySnapshotRuntimeContent(
        ConversationBinding binding,
        ConversationWorkspaceSnapshotOrigin origin)
    {
        if (origin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection)
        {
            return true;
        }

        return !RemoteConversationPersistencePolicy.IsRemoteBacked(
            binding.RemoteSessionId,
            binding.BoundProfileId);
    }

    private static bool ShouldPreserveAuthoritativeRuntimeProjection(
        ConversationBinding binding,
        bool canApplyRuntimeContent)
    {
        return !canApplyRuntimeContent
            && binding.SnapshotOrigin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection;
    }

    private static void ApplySnapshotRuntimeContentCore(
        ConversationBinding binding,
        ConversationWorkspaceSnapshot snapshot)
    {
        binding.Transcript.AddRange(CloneMessages(snapshot.Transcript));
        binding.Plan.AddRange(snapshot.Plan.Select(ClonePlanEntry));
        binding.AvailableModes.AddRange((snapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>()).Select(CloneModeOption));
        binding.SelectedModeId = snapshot.SelectedModeId;
        binding.ConfigOptions.AddRange((snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>()).Select(CloneConfigOption));
        binding.ShowConfigOptionsPanel = snapshot.ShowConfigOptionsPanel;
        binding.AvailableCommands.AddRange((snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>()).Select(CloneAvailableCommand));
        binding.Usage = CloneUsage(snapshot.Usage);
        binding.ShowPlanPanel = snapshot.ShowPlanPanel;
    }

    private static void ApplySnapshotIdentityCore(
        ConversationBinding binding,
        ConversationWorkspaceSnapshot snapshot,
        ConversationWorkspaceSnapshotOrigin origin)
    {
        binding.SnapshotOrigin = origin;
        binding.SnapshotConnectionInstanceId = origin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection
            ? snapshot.ConnectionInstanceId
            : null;
    }

    public async Task ApplySessionInfoUpdateAsync(
        string conversationId,
        string? title,
        DateTime? updatedAtUtc,
        string? cwd = null,
        bool allowRegisterWhenMissing = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await ApplySessionInfoSnapshotAsync(
            conversationId,
            new ConversationSessionInfoSnapshot
            {
                Title = title,
                HasTitle = true,
                Cwd = cwd,
                UpdatedAtUtc = updatedAtUtc,
                HasUpdatedAt = true
            },
            allowRegisterWhenMissing,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplySessionInfoSnapshotAsync(
        string conversationId,
        ConversationSessionInfoSnapshot sessionInfo,
        bool allowRegisterWhenMissing = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId) || sessionInfo is null)
        {
            return;
        }

        var knownConversation = false;
        var tombstonedConversation = false;
        await PostToContextAsync(() =>
        {
            lock (_stateGate)
            {
                knownConversation = _conversationBindings.ContainsKey(conversationId);
                tombstonedConversation = _deletedConversationTombstones.Contains(conversationId);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (tombstonedConversation)
        {
            _logger.LogDebug(
                "Ignore session info update for deleted conversation. ConversationId={ConversationId}",
                conversationId);
            return;
        }

        if (!knownConversation && !allowRegisterWhenMissing)
        {
            _logger.LogDebug(
                "Ignore session info update for unknown conversation. ConversationId={ConversationId}",
                conversationId);
            return;
        }

        if (_sessionManager.GetSession(conversationId) == null)
        {
            try
            {
                await _sessionManager.CreateSessionAsync(conversationId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to create missing session for session info update (ConversationId={ConversationId})",
                    conversationId);
            }
        }

        var shouldPersistMetadata = false;
        await PostToContextAsync(() =>
        {
            var conversationListChanged = false;
            lock (_stateGate)
            {
                if (!_conversationBindings.TryGetValue(conversationId, out var binding))
                {
                    if (!allowRegisterWhenMissing)
                    {
                        return;
                    }

                    binding = RegisterConversationCore(
                        conversationId,
                        default,
                        sessionInfo.UpdatedAtUtc ?? DateTime.UtcNow,
                        bumpVersion: true,
                        clearTombstone: false,
                        out var registeredConversationListChanged);
                    conversationListChanged |= registeredConversationListChanged;
                }

                var metadataChanged = false;
                var knownSessionInfo = EnsureSessionInfoCarriesEstablishedCwd(
                    binding.SessionInfo,
                    ResolveEstablishedConversationCwd(binding));
                var mergedSessionInfo = ConversationSessionInfoSnapshots.Merge(knownSessionInfo, sessionInfo);

                if (!SessionInfoEquals(binding.SessionInfo, mergedSessionInfo))
                {
                    binding.SessionInfo = mergedSessionInfo;
                    metadataChanged = true;
                }

                if (mergedSessionInfo.HasTitle)
                {
                    var sanitized = string.IsNullOrWhiteSpace(mergedSessionInfo.Title)
                        ? string.Empty
                        : SessionNamePolicy.Sanitize(mergedSessionInfo.Title);
                    var finalName = string.IsNullOrWhiteSpace(sanitized)
                        ? SessionNamePolicy.CreateDefault(conversationId)
                        : sanitized;

                    if (_sessionManager.UpdateSession(conversationId, session => session.DisplayName = finalName, updateActivity: false))
                    {
                        metadataChanged = true;
                    }
                }

                var normalizedCwd = string.IsNullOrWhiteSpace(mergedSessionInfo?.Cwd) ? null : mergedSessionInfo.Cwd.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedCwd))
                {
                    var existingCwd = _sessionManager.GetSession(conversationId)?.Cwd?.Trim();
                    if (!string.Equals(existingCwd, normalizedCwd, StringComparison.Ordinal)
                        && _sessionManager.UpdateSession(conversationId, session => session.Cwd = normalizedCwd, updateActivity: false))
                    {
                        metadataChanged = true;
                    }
                }

                if (mergedSessionInfo?.UpdatedAtUtc is DateTime parsedUpdatedAt
                    && parsedUpdatedAt != default
                    && parsedUpdatedAt > binding.LastUpdatedAt)
                {
                    binding.LastUpdatedAt = parsedUpdatedAt;
                    metadataChanged = true;
                }

                if (metadataChanged)
                {
                    conversationListChanged = true;
                    shouldPersistMetadata = true;
                }
            }

            if (conversationListChanged)
            {
                NotifyConversationListChanged();
            }
        }, cancellationToken).ConfigureAwait(false);

        if (shouldPersistMetadata)
        {
            // Authoritative session metadata (cwd/title/additionalDirectories) is recovery-critical for
            // project affinity and catalog restore. Persist immediately instead of relying on delayed save.
            await PersistRecoveryMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task PersistRecoveryMetadataAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return PersistRecoveryMetadataCoreAsync(cancellationToken);
    }

    public Task RegisterConversationAsync(
        string conversationId,
        DateTime? createdAt = null,
        DateTime? lastUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Task.CompletedTask;
        }

        var trimmedId = conversationId.Trim();
        var actualCreatedAt = createdAt ?? default;
        var actualLastUpdatedAt = lastUpdatedAt ?? DateTime.UtcNow;

        return PostToContextAsync(() =>
        {
            RegisterConversation(trimmedId, actualCreatedAt, actualLastUpdatedAt, bumpVersion: true, clearTombstone: true);
        }, cancellationToken);
    }

    public void ScheduleSave()
    {
        ThrowIfDisposed();
        if (_preferences.SaveLocalHistory == false)
        {
            return;
        }

        CancelScheduledSave();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token).ConfigureAwait(false);
                await SaveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled workspace save failed");
            }
        }, token);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        PersistedConversationState[] conversationStates;
        string[] deletedConversationIds;
        lock (_stateGate)
        {
            if (!_recoveryDocumentRestored)
            {
                // The in-memory catalog is only authoritative after a successful restore.
                // Persisting before that would overwrite the stored history with unhydrated state.
                throw new InvalidOperationException(
                    "Conversation workspace has not restored the persisted document; refusing to save.");
            }

            deletedConversationIds = _deletedConversationTombstones
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            conversationStates = _conversationBindings.Values
                .OrderByDescending(ResolveCatalogUpdatedAt)
                .ThenByDescending(item => item.LastUpdatedAt)
                .Select(binding =>
                {
                    var shouldPersistRuntimeContent = RemoteConversationPersistencePolicy.ShouldPersistRuntimeContent(
                        binding.RemoteSessionId,
                        binding.BoundProfileId);
                    return new PersistedConversationState(
                        binding.ConversationId,
                        binding.CreatedAt,
                        binding.LastUpdatedAt,
                        binding.LastAccessedAt,
                        binding.RemoteSessionId,
                        binding.BoundProfileId,
                        binding.ProjectAffinityOverride?.ProjectId,
                        shouldPersistRuntimeContent ? binding.SelectedModeId : null,
                        shouldPersistRuntimeContent && binding.ShowConfigOptionsPanel,
                        shouldPersistRuntimeContent && binding.ShowPlanPanel,
                        shouldPersistRuntimeContent ? CloneMessages(binding.Transcript).ToArray() : [],
                        shouldPersistRuntimeContent ? binding.AvailableModes.Select(CloneModeOption).ToArray() : [],
                        shouldPersistRuntimeContent ? binding.ConfigOptions.Select(CloneConfigOption).ToArray() : [],
                        shouldPersistRuntimeContent ? binding.Plan.Select(ClonePlanEntry).ToArray() : [],
                        shouldPersistRuntimeContent ? binding.AvailableCommands.Select(CloneAvailableCommand).ToArray() : [],
                        ConversationSessionInfoSnapshots.Clone(binding.SessionInfo),
                        shouldPersistRuntimeContent ? CloneUsage(binding.Usage) : null);
                })
                .ToArray();
        }

        var document = new ConversationDocument
        {
            Version = 4,
            LastActiveConversationId = null
        };

        document.DeletedConversationIds.AddRange(deletedConversationIds);
        foreach (var conversationState in conversationStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessionManager.GetSession(conversationState.ConversationId);
            var record = new ConversationRecord
            {
                ConversationId = conversationState.ConversationId,
                DisplayName = ResolveSessionDisplayName(conversationState.ConversationId),
                CreatedAt = conversationState.CreatedAt,
                LastUpdatedAt = conversationState.LastUpdatedAt,
                LastAccessedAt = conversationState.LastAccessedAt == default
                    ? conversationState.LastUpdatedAt
                    : conversationState.LastAccessedAt,
                Cwd = session?.Cwd,
                RemoteSessionId = conversationState.RemoteSessionId,
                BoundProfileId = conversationState.BoundProfileId,
                ProjectAffinityOverrideProjectId = conversationState.ProjectAffinityOverrideProjectId,
                SelectedModeId = conversationState.SelectedModeId,
                ShowConfigOptionsPanel = conversationState.ShowConfigOptionsPanel,
                SessionInfo = ConversationSessionInfoSnapshots.Clone(conversationState.SessionInfo),
                Usage = CloneUsage(conversationState.Usage),
                ShowPlanPanel = conversationState.ShowPlanPanel
            };

            record.Messages.AddRange(conversationState.Transcript);
            record.AvailableModes.AddRange(conversationState.AvailableModes);
            record.ConfigOptions.AddRange(conversationState.ConfigOptions);
            record.AvailableCommands.AddRange(conversationState.AvailableCommands);
            record.Plan.AddRange(conversationState.Plan);

            document.Conversations.Add(record);
        }

        return _conversationStore.SaveAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelScheduledSave();
        _sessionSwitchGate.Dispose();
        _saveGate.Dispose();
    }

    private void CancelScheduledSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
    }

    private Task PersistRecoveryMetadataCoreAsync(CancellationToken cancellationToken)
    {
        if (_preferences.SaveLocalHistory == false)
        {
            return Task.CompletedTask;
        }

        CancelScheduledSave();
        return SaveAsync(cancellationToken);
    }

    private void ApplyRestoredDocument(ConversationDocument document)
    {
        lock (_stateGate)
        {
            _deletedConversationTombstones.Clear();
            if (document.DeletedConversationIds is { Count: > 0 })
            {
                foreach (var deletedId in document.DeletedConversationIds)
                {
                    if (!string.IsNullOrWhiteSpace(deletedId))
                    {
                        _deletedConversationTombstones.Add(deletedId.Trim());
                    }
                }
            }

            foreach (var conversation in document.Conversations)
            {
                if (string.IsNullOrWhiteSpace(conversation.ConversationId))
                {
                    continue;
                }

                var binding = RegisterConversationCore(
                    conversation.ConversationId,
                    conversation.CreatedAt,
                    conversation.LastUpdatedAt,
                    bumpVersion: false,
                    clearTombstone: true,
                    out _);
                binding.LastAccessedAt = conversation.LastAccessedAt == default
                    ? binding.LastUpdatedAt
                    : conversation.LastAccessedAt;
                var shouldRestoreRuntimeContent = RemoteConversationPersistencePolicy.ShouldRestoreRuntimeContent(
                    conversation.RemoteSessionId,
                    conversation.BoundProfileId);
                binding.Transcript.Clear();
                if (shouldRestoreRuntimeContent)
                {
                    binding.Transcript.AddRange(CloneMessages(conversation.Messages));
                }

                binding.Plan.Clear();
                if (shouldRestoreRuntimeContent)
                {
                    binding.Plan.AddRange((conversation.Plan ?? []).Select(ClonePlanEntry));
                }

                binding.AvailableModes.Clear();
                if (shouldRestoreRuntimeContent)
                {
                    binding.AvailableModes.AddRange((conversation.AvailableModes ?? []).Select(CloneModeOption));
                }

                binding.SelectedModeId = shouldRestoreRuntimeContent ? conversation.SelectedModeId : null;
                binding.ConfigOptions.Clear();
                if (shouldRestoreRuntimeContent)
                {
                    binding.ConfigOptions.AddRange((conversation.ConfigOptions ?? []).Select(CloneConfigOption));
                }

                binding.ShowConfigOptionsPanel = shouldRestoreRuntimeContent && conversation.ShowConfigOptionsPanel;
                binding.AvailableCommands.Clear();
                if (shouldRestoreRuntimeContent)
                {
                    binding.AvailableCommands.AddRange((conversation.AvailableCommands ?? []).Select(CloneAvailableCommand));
                }

                var restoredCwd = ResolveConversationRecordCwd(conversation);
                binding.SessionInfo = EnsureSessionInfoCarriesEstablishedCwd(
                    ConversationSessionInfoSnapshots.Clone(conversation.SessionInfo),
                    restoredCwd);
                binding.Usage = shouldRestoreRuntimeContent ? CloneUsage(conversation.Usage) : null;
                binding.ShowPlanPanel = shouldRestoreRuntimeContent && conversation.ShowPlanPanel;
                binding.SnapshotOrigin = ConversationWorkspaceSnapshotOrigin.Restored;
                binding.RemoteSessionId = conversation.RemoteSessionId;
                binding.BoundProfileId = conversation.BoundProfileId;
                binding.ProjectAffinityOverride = string.IsNullOrWhiteSpace(conversation.ProjectAffinityOverrideProjectId)
                    ? null
                    : new ProjectAffinityOverride(conversation.ProjectAffinityOverrideProjectId);

                var displayName = ResolveRestoredDisplayName(conversation);

                _sessionManager.UpdateSession(
                    conversation.ConversationId,
                    session =>
                    {
                        session.DisplayName = displayName;
                        session.CreatedAt = binding.CreatedAt;
                        session.LastActivityAt = binding.LastAccessedAt > binding.LastUpdatedAt
                            ? binding.LastAccessedAt
                            : binding.LastUpdatedAt;
                        if (!string.IsNullOrWhiteSpace(restoredCwd))
                        {
                            session.Cwd = restoredCwd;
                        }
                    },
                    updateActivity: false);
            }

            var lastActiveConversationId = document.LastActiveConversationId;
            if (!string.IsNullOrWhiteSpace(lastActiveConversationId) && _conversationBindings.ContainsKey(lastActiveConversationId))
            {
                LastActiveConversationId = lastActiveConversationId;
            }
            else
            {
                LastActiveConversationId = _conversationBindings.Values
                    .OrderByDescending(binding => binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt)
                    .ThenByDescending(binding => binding.LastUpdatedAt)
                    .Select(binding => binding.ConversationId)
                    .FirstOrDefault();
            }

            _recoveryDocumentRestored = true;
        }
    }

    private void RemoveConversation(string conversationId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var removed = false;
        lock (_stateGate)
        {
            if (string.Equals(LastActiveConversationId, conversationId, StringComparison.Ordinal))
            {
                LastActiveConversationId = null;
            }

            _conversationBindings.Remove(conversationId);
            _deletedConversationTombstones.Add(conversationId);
            removed = true;
        }

        if (!removed)
        {
            return;
        }

        _sessionManager.RemoveSession(conversationId);
        NotifyConversationListChanged();
    }

    private void NotifyConversationListChanged()
    {
        ConversationListVersion++;
    }

    private bool UpdateLastAccessedAt(string conversationId, DateTime accessedAt)
    {
        lock (_stateGate)
        {
            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                return false;
            }

            binding.LastAccessedAt = accessedAt;
            return true;
        }
    }

    private ConversationBinding RegisterConversation(
        string conversationId,
        DateTime createdAt,
        DateTime lastUpdatedAt,
        bool bumpVersion,
        bool clearTombstone = false)
    {
        var conversationListChanged = false;
        ConversationBinding binding;
        lock (_stateGate)
        {
            binding = RegisterConversationCore(
                conversationId,
                createdAt,
                lastUpdatedAt,
                bumpVersion,
                clearTombstone,
                out conversationListChanged);
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }

        return binding;
    }

    private string ResolveSessionDisplayName(string conversationId)
    {
        var session = _sessionManager.GetSession(conversationId);
        var displayName = session?.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return SessionNamePolicy.CreateDefault(conversationId);
    }

    private ConversationBinding RegisterConversationCore(
        string conversationId,
        DateTime createdAt,
        DateTime lastUpdatedAt,
        bool bumpVersion,
        bool clearTombstone,
        out bool conversationListChanged)
    {
        var existed = _conversationBindings.ContainsKey(conversationId);
        var binding = GetOrCreateConversationBindingCore(conversationId);
        if (clearTombstone)
        {
            _deletedConversationTombstones.Remove(conversationId);
        }

        var previousLastUpdated = binding.LastUpdatedAt;
        if (createdAt != default)
        {
            binding.CreatedAt = createdAt;
        }

        var actualLastUpdated = lastUpdatedAt == default ? DateTime.UtcNow : lastUpdatedAt;
        binding.LastUpdatedAt = actualLastUpdated;
        conversationListChanged = bumpVersion && (!existed || actualLastUpdated != previousLastUpdated);
        return binding;
    }

    private ConversationBinding GetOrCreateConversationBindingCore(string conversationId)
    {
        if (_conversationBindings.TryGetValue(conversationId, out var existing))
        {
            return existing;
        }

        var created = new ConversationBinding(conversationId);
        _conversationBindings[conversationId] = created;
        return created;
    }

    private static ConversationMessageSnapshot CloneMessage(ConversationMessageSnapshot source)
        => new()
        {
            Id = source.Id,
            Timestamp = source.Timestamp,
            IsOutgoing = source.IsOutgoing,
            ContentType = source.ContentType,
            Title = source.Title,
            TextContent = source.TextContent,
            ImageData = source.ImageData,
            ImageMimeType = source.ImageMimeType,
            AudioData = source.AudioData,
            AudioMimeType = source.AudioMimeType,
            ProtocolMessageId = source.ProtocolMessageId,
            ToolCallId = source.ToolCallId,
            ToolCallKind = source.ToolCallKind,
            ToolCallStatus = source.ToolCallStatus,
            ToolCallJson = source.ToolCallJson,
            ToolCallRawInputJson = source.ToolCallRawInputJson,
            ToolCallRawOutputJson = source.ToolCallRawOutputJson,
            ToolCallContent = ToolCallContentSnapshots.CloneDomainPayload(source.ToolCallContent),
            ToolCallLocations = ToolCallContentSnapshots.CloneDomainPayload(source.ToolCallLocations),
            PlanEntry = source.PlanEntry is null ? null : ClonePlanEntry(source.PlanEntry),
            ModeId = source.ModeId
        };

    private static IEnumerable<ConversationMessageSnapshot> CloneMessages(IEnumerable<ConversationMessageSnapshot> source)
    {
        foreach (var message in source)
        {
            if (message is null)
            {
                continue;
            }

            yield return CloneMessage(message);
        }
    }

    private static ConversationPlanEntrySnapshot ClonePlanEntry(ConversationPlanEntrySnapshot source)
        => ConversationPlanWire.CloneDomain(source);

    private static ConversationModeOptionSnapshot CloneModeOption(ConversationModeOptionSnapshot source)
        => new()
        {
            ModeId = source.ModeId,
            ModeName = source.ModeName,
            Description = source.Description
        };

    private static ConversationConfigOptionSnapshot CloneConfigOption(ConversationConfigOptionSnapshot source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Category = source.Category,
            ValueType = source.ValueType,
            SelectedValue = source.SelectedValue,
            Options = (source.Options ?? [])
                .Select(CloneConfigOptionChoice)
                .ToList()
        };

    private static ConversationConfigOptionChoiceSnapshot CloneConfigOptionChoice(ConversationConfigOptionChoiceSnapshot source)
        => new()
        {
            Value = source.Value,
            Name = source.Name,
            Description = source.Description
        };

    private static ConversationAvailableCommandSnapshot CloneAvailableCommand(ConversationAvailableCommandSnapshot source)
        => new(source.Name, source.Description, source.InputHint);

    private static ConversationUsageSnapshot? CloneUsage(ConversationUsageSnapshot? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ConversationUsageSnapshot(
            source.Used,
            source.Size,
            source.Cost is null
                ? null
                : new ConversationUsageCostSnapshot(source.Cost.Amount, source.Cost.Currency));
    }

    private static bool SessionInfoEquals(ConversationSessionInfoSnapshot? left, ConversationSessionInfoSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (!string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            || left.HasTitle != right.HasTitle
            || !string.Equals(left.Cwd, right.Cwd, StringComparison.Ordinal)
            || !AdditionalDirectorySequencesEqual(left.AdditionalDirectories, right.AdditionalDirectories)
            || left.UpdatedAtUtc != right.UpdatedAtUtc
            || left.HasUpdatedAt != right.HasUpdatedAt)
        {
            return false;
        }

        if (left.Meta is null || right.Meta is null)
        {
            return left.Meta is null && right.Meta is null;
        }

        if (left.Meta.Count != right.Meta.Count)
        {
            return false;
        }

        foreach (var pair in left.Meta)
        {
            if (!right.Meta.TryGetValue(pair.Key, out var rightValue)
                || !Equals(pair.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AdditionalDirectorySequencesEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private async Task PostToContextAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _uiDispatcher.EnqueueAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ConversationSessionInfoSnapshot? EnsureSessionInfoCarriesEstablishedCwd(
        ConversationSessionInfoSnapshot? sessionInfo,
        string? establishedCwd)
    {
        var normalizedCwd = string.IsNullOrWhiteSpace(establishedCwd) ? null : establishedCwd.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return sessionInfo;
        }

        if (sessionInfo is null)
        {
            return new ConversationSessionInfoSnapshot
            {
                Cwd = normalizedCwd
            };
        }

        if (!string.IsNullOrWhiteSpace(sessionInfo.Cwd))
        {
            return sessionInfo;
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = sessionInfo.Title,
            HasTitle = sessionInfo.HasTitle,
            Cwd = normalizedCwd,
            AdditionalDirectories = sessionInfo.AdditionalDirectories is null
                ? null
                : new List<string>(sessionInfo.AdditionalDirectories),
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
            HasUpdatedAt = sessionInfo.HasUpdatedAt,
            Meta = sessionInfo.Meta is null
                ? null
                : new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
        };
    }

    private static string ResolveRestoredDisplayName(ConversationRecord conversation)
    {
        var remoteTitle = conversation.SessionInfo?.HasTitle == true
            ? conversation.SessionInfo.Title
            : null;
        var sanitized = string.IsNullOrWhiteSpace(remoteTitle)
            ? string.Empty
            : SessionNamePolicy.Sanitize(remoteTitle);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        if (!RemoteConversationPersistencePolicy.IsRemoteBacked(conversation.RemoteSessionId, conversation.BoundProfileId))
        {
            var localDisplayName = SessionNamePolicy.Sanitize(conversation.DisplayName);
            if (!string.IsNullOrWhiteSpace(localDisplayName))
            {
                return localDisplayName;
            }
        }

        return SessionNamePolicy.CreateDefault(conversation.ConversationId);
    }

    private static string? ResolveConversationRecordCwd(ConversationRecord conversation)
    {
        if (!string.IsNullOrWhiteSpace(conversation.Cwd))
        {
            return conversation.Cwd.Trim();
        }

        if (!string.IsNullOrWhiteSpace(conversation.SessionInfo?.Cwd))
        {
            return conversation.SessionInfo.Cwd.Trim();
        }

        return null;
    }

    private static DateTime ResolveCatalogUpdatedAt(ConversationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            && binding.SessionInfo?.UpdatedAtUtc is DateTime remoteUpdatedAt
            && remoteUpdatedAt != default)
        {
            return remoteUpdatedAt;
        }

        return binding.LastUpdatedAt == default ? binding.CreatedAt : binding.LastUpdatedAt;
    }

    private string? ResolveEstablishedConversationCwd(ConversationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!string.IsNullOrWhiteSpace(binding.SessionInfo?.Cwd))
        {
            return binding.SessionInfo.Cwd.Trim();
        }

        var localCwd = _sessionManager.GetSession(binding.ConversationId)?.Cwd;
        if (!string.IsNullOrWhiteSpace(localCwd))
        {
            return localCwd.Trim();
        }

        if (!string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            var remoteCwd = _sessionManager.GetSession(binding.RemoteSessionId)?.Cwd;
            if (!string.IsNullOrWhiteSpace(remoteCwd))
            {
                return remoteCwd.Trim();
            }
        }

        return null;
    }

    private sealed record PersistedConversationState(
        string ConversationId,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        DateTime LastAccessedAt,
        string? RemoteSessionId,
        string? BoundProfileId,
        string? ProjectAffinityOverrideProjectId,
        string? SelectedModeId,
        bool ShowConfigOptionsPanel,
        bool ShowPlanPanel,
        ConversationMessageSnapshot[] Transcript,
        ConversationModeOptionSnapshot[] AvailableModes,
        ConversationConfigOptionSnapshot[] ConfigOptions,
        ConversationPlanEntrySnapshot[] Plan,
        ConversationAvailableCommandSnapshot[] AvailableCommands,
        ConversationSessionInfoSnapshot? SessionInfo,
        ConversationUsageSnapshot? Usage);

    private sealed class ConversationBinding
    {
        public ConversationBinding(string conversationId)
        {
            ConversationId = conversationId;
            CreatedAt = DateTime.UtcNow;
            LastUpdatedAt = DateTime.UtcNow;
            LastAccessedAt = DateTime.UtcNow;
        }

        public string ConversationId { get; }

        public string? BoundProfileId { get; set; }

        public string? RemoteSessionId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime LastUpdatedAt { get; set; }

        public DateTime LastAccessedAt { get; set; }

        public string? SelectedModeId { get; set; }

        public List<ConversationModeOptionSnapshot> AvailableModes { get; } = new();

        public List<ConversationConfigOptionSnapshot> ConfigOptions { get; } = new();

        public bool ShowConfigOptionsPanel { get; set; }

        public List<ConversationAvailableCommandSnapshot> AvailableCommands { get; } = new();

        public ConversationSessionInfoSnapshot? SessionInfo { get; set; }

        public ConversationUsageSnapshot? Usage { get; set; }

        public List<ConversationMessageSnapshot> Transcript { get; } = new();

        public List<ConversationPlanEntrySnapshot> Plan { get; } = new();

        public bool ShowPlanPanel { get; set; }

        public ProjectAffinityOverride? ProjectAffinityOverride { get; set; }

        public ConversationWorkspaceSnapshotOrigin SnapshotOrigin { get; set; }

        public string? SnapshotConnectionInstanceId { get; set; }

    }
}

public enum ConversationWorkspaceSnapshotOrigin
{
    Restored = 0,
    RuntimeProjection = 1
}

public sealed record ConversationWorkspaceSnapshot(
    string ConversationId,
    IReadOnlyList<ConversationMessageSnapshot> Transcript,
    IReadOnlyList<ConversationPlanEntrySnapshot> Plan,
    bool ShowPlanPanel,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    IReadOnlyList<ConversationModeOptionSnapshot>? AvailableModes = null,
    string? SelectedModeId = null,
    IReadOnlyList<ConversationConfigOptionSnapshot>? ConfigOptions = null,
    bool ShowConfigOptionsPanel = false,
    IReadOnlyList<ConversationAvailableCommandSnapshot>? AvailableCommands = null,
    ConversationSessionInfoSnapshot? SessionInfo = null,
    ConversationUsageSnapshot? Usage = null,
    string? ConnectionInstanceId = null);

public sealed record ConversationRemoteBindingState(
    string ConversationId,
    string? RemoteSessionId,
    string? BoundProfileId);

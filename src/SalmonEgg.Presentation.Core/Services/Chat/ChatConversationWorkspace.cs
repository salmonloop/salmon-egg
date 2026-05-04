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
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatConversationWorkspace : ObservableObject, IConversationCatalog, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
    private readonly IConversationWorkspacePreferences _preferences;
    private readonly INavigationProjectPreferences? _navigationProjectPreferences;
    private readonly ILogger<ChatConversationWorkspace> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private readonly Dictionary<string, ConversationBinding> _conversationBindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedConversationTombstones = new(StringComparer.Ordinal);
    private CancellationTokenSource? _saveCts;
    private bool _disposed;
    private bool _isConversationListLoading = true;
    private int _conversationListVersion;
    private string? _lastActiveConversationId;

    public ChatConversationWorkspace(
        ISessionManager sessionManager,
        IConversationStore conversationStore,
        IConversationWorkspacePreferences preferences,
        ILogger<ChatConversationWorkspace> logger,
        IUiDispatcher uiDispatcher,
        INavigationProjectPreferences? navigationProjectPreferences = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationProjectPreferences = navigationProjectPreferences;
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
            _logger.LogWarning(ex, "Failed to restore conversation workspace");
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

            if (_sessionManager.GetSession(conversation.ConversationId) != null)
            {
                continue;
            }

            try
            {
                await _sessionManager.CreateSessionAsync(conversation.ConversationId, conversation.Cwd).ConfigureAwait(false);
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
                .OrderByDescending(binding => binding.LastUpdatedAt)
                .Select(binding => binding.ConversationId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }
    }

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
    {
        var options = new List<ConversationProjectTargetOption>
        {
            new(NavigationProjectIds.Unclassified, "未归类")
        };

        if (_navigationProjectPreferences == null)
        {
            return options;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            NavigationProjectIds.Unclassified
        };
        foreach (var project in _navigationProjectPreferences.Projects
                     .Where(project => project != null
                         && !string.IsNullOrWhiteSpace(project.ProjectId)
                         && !string.IsNullOrWhiteSpace(project.Name))
                     .OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(project.ProjectId))
            {
                continue;
            }

            options.Add(new ConversationProjectTargetOption(project.ProjectId, project.Name));
        }

        return options;
    }

    public IReadOnlyList<ConversationCatalogItem> GetCatalog()
    {
        lock (_stateGate)
        {
            return _conversationBindings.Values
                .OrderByDescending(binding => binding.LastUpdatedAt)
                .Select(binding => new ConversationCatalogItem(
                    binding.ConversationId,
                    ResolveSessionDisplayName(binding.ConversationId),
                    ResolveEstablishedConversationCwd(binding),
                    binding.CreatedAt,
                    binding.LastUpdatedAt,
                    binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt,
                    binding.RemoteSessionId,
                    binding.BoundProfileId,
                    binding.ProjectAffinityOverride?.ProjectId))
                .ToArray();
        }
    }

    public void RenameConversation(string conversationId, string newDisplayName)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var sanitized = SessionNamePolicy.Sanitize(newDisplayName);
        var finalName = string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(conversationId)
            : sanitized;

        if (!_sessionManager.UpdateSession(conversationId, session => session.DisplayName = finalName, updateActivity: false))
        {
            return;
        }

        var conversationListChanged = false;
        lock (_stateGate)
        {
            if (_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                binding.LastUpdatedAt = DateTime.UtcNow;
                conversationListChanged = true;
            }
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }

        ScheduleSave();
    }

    public void MoveConversationToProject(string conversationId, string projectId)
        => UpdateProjectAffinityOverride(conversationId, projectId);

    public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        RemoveConversation(conversationId);
        return Task.FromResult(new ConversationMutationResult(true, false, null));
    }

    public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        RemoveConversation(conversationId);
        return Task.FromResult(new ConversationMutationResult(true, false, null));
    }

    public void ArchiveConversation(string conversationId)
        => RemoveConversation(conversationId);

    public void DeleteConversation(string conversationId)
        => RemoveConversation(conversationId);

    public async Task<bool> TrySwitchToSessionAsync(string sessionId, CancellationToken cancellationToken = default)
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
            _logger.LogError(ex, "Switching workspace conversation failed (ConversationId={ConversationId})", sessionId);
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
                binding.PlanTitle,
                binding.CreatedAt,
                binding.LastUpdatedAt,
                binding.AvailableModes.Select(CloneModeOption).ToArray(),
                binding.SelectedModeId,
                binding.ConfigOptions.Select(CloneConfigOption).ToArray(),
                binding.ShowConfigOptionsPanel,
                binding.AvailableCommands.Select(CloneAvailableCommand).ToArray(),
                ConversationSessionInfoSnapshots.Clone(binding.SessionInfo),
                CloneUsage(binding.Usage));
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

    public void UpsertConversationSnapshot(ConversationWorkspaceSnapshot snapshot)
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
            binding.Transcript.Clear();
            binding.Transcript.AddRange(CloneMessages(snapshot.Transcript));
            binding.Plan.Clear();
            binding.Plan.AddRange(snapshot.Plan.Select(ClonePlanEntry));
            binding.AvailableModes.Clear();
            binding.AvailableModes.AddRange((snapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>()).Select(CloneModeOption));
            binding.SelectedModeId = snapshot.SelectedModeId;
            binding.ConfigOptions.Clear();
            binding.ConfigOptions.AddRange((snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>()).Select(CloneConfigOption));
            binding.ShowConfigOptionsPanel = snapshot.ShowConfigOptionsPanel;
            binding.AvailableCommands.Clear();
            binding.AvailableCommands.AddRange((snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>()).Select(CloneAvailableCommand));
            var mergedSessionInfo = snapshot.SessionInfo is null
                ? ConversationSessionInfoSnapshots.Clone(binding.SessionInfo)
                : ConversationSessionInfoSnapshots.Merge(binding.SessionInfo, snapshot.SessionInfo);
            binding.SessionInfo = EnsureSessionInfoCarriesEstablishedCwd(
                mergedSessionInfo,
                ResolveEstablishedConversationCwd(binding));
            binding.Usage = CloneUsage(snapshot.Usage);
            binding.ShowPlanPanel = snapshot.ShowPlanPanel;
            binding.PlanTitle = snapshot.PlanTitle;
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
        }

        if (conversationListChanged)
        {
            NotifyConversationListChanged();
        }
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
                Cwd = cwd,
                UpdatedAtUtc = updatedAtUtc
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

        await PostToContextAsync(() =>
        {
            var conversationListChanged = false;
            var shouldSave = false;
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
                var mergedSessionInfo = ConversationSessionInfoSnapshots.Merge(binding.SessionInfo, sessionInfo);
                var establishedCwd = ResolveEstablishedConversationCwd(binding)?.Trim();

                if (!string.IsNullOrWhiteSpace(establishedCwd)
                    && !string.Equals(mergedSessionInfo.Cwd?.Trim(), establishedCwd, StringComparison.Ordinal))
                {
                    // ACP session/list and session_info_update are metadata channels and must not
                    // overwrite the cwd established by session/new or session/load.
                    mergedSessionInfo = new ConversationSessionInfoSnapshot
                    {
                        Title = mergedSessionInfo.Title,
                        Description = mergedSessionInfo.Description,
                        Cwd = establishedCwd,
                        UpdatedAtUtc = mergedSessionInfo.UpdatedAtUtc,
                        Meta = mergedSessionInfo.Meta is null
                            ? null
                            : new Dictionary<string, object?>(mergedSessionInfo.Meta, StringComparer.Ordinal)
                    };
                }

                if (!SessionInfoEquals(binding.SessionInfo, mergedSessionInfo))
                {
                    binding.SessionInfo = mergedSessionInfo;
                    metadataChanged = true;
                }

                if (!string.IsNullOrWhiteSpace(mergedSessionInfo?.Title))
                {
                    var sanitized = SessionNamePolicy.Sanitize(mergedSessionInfo.Title);
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
                    shouldSave = true;
                }
            }

            if (conversationListChanged)
            {
                NotifyConversationListChanged();
            }

            if (shouldSave)
            {
                ScheduleSave();
            }
        }, cancellationToken).ConfigureAwait(false);
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

        _saveCts?.Cancel();
        _saveCts?.Dispose();
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
                _logger.LogDebug(ex, "Scheduled workspace save failed");
            }
        }, token);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        PersistedConversationState[] conversationStates;
        string[] deletedConversationIds;
        lock (_stateGate)
        {
            deletedConversationIds = _deletedConversationTombstones
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            conversationStates = _conversationBindings.Values
                .OrderByDescending(item => item.LastUpdatedAt)
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
                        shouldPersistRuntimeContent ? binding.PlanTitle : null,
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
                ShowPlanPanel = conversationState.ShowPlanPanel,
                PlanTitle = conversationState.PlanTitle
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
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
        _sessionSwitchGate.Dispose();
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

                binding.SessionInfo = EnsureSessionInfoCarriesEstablishedCwd(
                    ConversationSessionInfoSnapshots.Clone(conversation.SessionInfo),
                    conversation.Cwd);
                binding.Usage = shouldRestoreRuntimeContent ? CloneUsage(conversation.Usage) : null;
                binding.ShowPlanPanel = shouldRestoreRuntimeContent && conversation.ShowPlanPanel;
                binding.PlanTitle = shouldRestoreRuntimeContent ? conversation.PlanTitle : null;
                binding.RemoteSessionId = conversation.RemoteSessionId;
                binding.BoundProfileId = conversation.BoundProfileId;
                binding.ProjectAffinityOverride = string.IsNullOrWhiteSpace(conversation.ProjectAffinityOverrideProjectId)
                    ? null
                    : new ProjectAffinityOverride(conversation.ProjectAffinityOverrideProjectId);

                var displayName = string.IsNullOrWhiteSpace(conversation.DisplayName)
                    ? SessionNamePolicy.CreateDefault(conversation.ConversationId)
                    : conversation.DisplayName.Trim();

                _sessionManager.UpdateSession(
                    conversation.ConversationId,
                    session =>
                    {
                        session.DisplayName = displayName;
                        session.CreatedAt = binding.CreatedAt;
                        session.LastActivityAt = binding.LastAccessedAt > binding.LastUpdatedAt
                            ? binding.LastAccessedAt
                            : binding.LastUpdatedAt;
                        if (!string.IsNullOrWhiteSpace(conversation.Cwd))
                        {
                            session.Cwd = conversation.Cwd;
                        }
                    },
                    updateActivity: false);
            }

            var lastActiveConversationId = document.LastActiveConversationId;
            if (!string.IsNullOrWhiteSpace(lastActiveConversationId) && _conversationBindings.ContainsKey(lastActiveConversationId))
            {
                LastActiveConversationId = lastActiveConversationId;
                return;
            }

            LastActiveConversationId = _conversationBindings.Values
                .OrderByDescending(binding => binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt)
                .ThenByDescending(binding => binding.LastUpdatedAt)
                .Select(binding => binding.ConversationId)
                .FirstOrDefault();
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
        ScheduleSave();
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
            ToolCallContent = ToolCallContentSnapshots.CloneList(source.ToolCallContent),
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
        => new()
        {
            Content = source.Content,
            Status = source.Status,
            Priority = source.Priority
        };

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
            || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            || !string.Equals(left.Cwd, right.Cwd, StringComparison.Ordinal)
            || left.UpdatedAtUtc != right.UpdatedAtUtc)
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
            Description = sessionInfo.Description,
            Cwd = normalizedCwd,
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
            Meta = sessionInfo.Meta is null
                ? null
                : new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
        };
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
        string? PlanTitle,
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

        public string? PlanTitle { get; set; }

        public ProjectAffinityOverride? ProjectAffinityOverride { get; set; }
    }
}

public sealed record ConversationWorkspaceSnapshot(
    string ConversationId,
    IReadOnlyList<ConversationMessageSnapshot> Transcript,
    IReadOnlyList<ConversationPlanEntrySnapshot> Plan,
    bool ShowPlanPanel,
    string? PlanTitle,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    IReadOnlyList<ConversationModeOptionSnapshot>? AvailableModes = null,
    string? SelectedModeId = null,
    IReadOnlyList<ConversationConfigOptionSnapshot>? ConfigOptions = null,
    bool ShowConfigOptionsPanel = false,
    IReadOnlyList<ConversationAvailableCommandSnapshot>? AvailableCommands = null,
    ConversationSessionInfoSnapshot? SessionInfo = null,
    ConversationUsageSnapshot? Usage = null);

public sealed record ConversationRemoteBindingState(
    string ConversationId,
    string? RemoteSessionId,
    string? BoundProfileId);

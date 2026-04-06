using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatConversationWorkspace : ObservableObject, IConversationCatalog, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
    private readonly IConversationWorkspacePreferences _preferences;
    private readonly INavigationProjectPreferences? _navigationProjectPreferences;
    private readonly ILogger<ChatConversationWorkspace> _logger;
    private readonly SynchronizationContext _syncContext;
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
        SynchronizationContext? syncContext = null,
        INavigationProjectPreferences? navigationProjectPreferences = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _navigationProjectPreferences = navigationProjectPreferences;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _syncContext = syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
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
        => _conversationBindings.Values
            .OrderByDescending(binding => binding.LastUpdatedAt)
            .Select(binding => binding.ConversationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

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
        => _conversationBindings.Values
            .OrderByDescending(binding => binding.LastUpdatedAt)
            .Select(binding => new ConversationCatalogItem(
                binding.ConversationId,
                ResolveSessionDisplayName(binding.ConversationId),
                _sessionManager.GetSession(binding.ConversationId)?.Cwd,
                binding.CreatedAt,
                binding.LastUpdatedAt,
                binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt,
                binding.RemoteSessionId,
                binding.BoundProfileId,
                binding.ProjectAffinityOverride?.ProjectId))
            .ToArray();

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

        if (_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            binding.LastUpdatedAt = DateTime.UtcNow;
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
                UpdateLastAccessedAt(sessionId, DateTime.UtcNow);
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
            binding.ShowConfigOptionsPanel);
    }

    public ConversationRemoteBindingState? GetRemoteBinding(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            return null;
        }

        return new ConversationRemoteBindingState(binding.ConversationId, binding.RemoteSessionId, binding.BoundProfileId);
    }

    public ProjectAffinityOverride? GetProjectAffinityOverride(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            return null;
        }

        return binding.ProjectAffinityOverride;
    }

    public void UpdateProjectAffinityOverride(string conversationId, string? projectId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (!_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            binding = RegisterConversation(conversationId, default, DateTime.UtcNow, bumpVersion: true);
        }

        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
        var newOverride = normalizedProjectId is null ? null : new ProjectAffinityOverride(normalizedProjectId);
        if (Equals(binding.ProjectAffinityOverride, newOverride))
        {
            return;
        }

        binding.ProjectAffinityOverride = newOverride;
        binding.LastUpdatedAt = DateTime.UtcNow;
        NotifyConversationListChanged();
        ScheduleSave();
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

        if (!_conversationBindings.ContainsKey(snapshot.ConversationId)
            && _deletedConversationTombstones.Contains(snapshot.ConversationId))
        {
            _logger.LogDebug(
                "Ignore snapshot upsert for deleted conversation. ConversationId={ConversationId}",
                snapshot.ConversationId);
            return;
        }

        var binding = RegisterConversation(
            snapshot.ConversationId,
            snapshot.CreatedAt,
            snapshot.LastUpdatedAt,
            bumpVersion: true);
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
        binding.ShowPlanPanel = snapshot.ShowPlanPanel;
        binding.PlanTitle = snapshot.PlanTitle;
    }

    public void UpdateRemoteBinding(string conversationId, string? remoteSessionId, string? boundProfileId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

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
            binding = RegisterConversation(conversationId, default, DateTime.UtcNow, bumpVersion: true);
        }

        binding.RemoteSessionId = remoteSessionId;
        binding.BoundProfileId = boundProfileId;
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

        var knownConversation = false;
        var tombstonedConversation = false;
        await PostToContextAsync(() =>
        {
            knownConversation = _conversationBindings.ContainsKey(conversationId);
            tombstonedConversation = _deletedConversationTombstones.Contains(conversationId);
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
            if (!_conversationBindings.TryGetValue(conversationId, out var binding))
            {
                if (!allowRegisterWhenMissing)
                {
                    return;
                }

                binding = RegisterConversation(conversationId, default, updatedAtUtc ?? DateTime.UtcNow, bumpVersion: true);
            }

            var metadataChanged = false;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var sanitized = SessionNamePolicy.Sanitize(title);
                var finalName = string.IsNullOrWhiteSpace(sanitized)
                    ? SessionNamePolicy.CreateDefault(conversationId)
                    : sanitized;

                if (_sessionManager.UpdateSession(conversationId, session => session.DisplayName = finalName, updateActivity: false))
                {
                    metadataChanged = true;
                }
            }

            var normalizedCwd = string.IsNullOrWhiteSpace(cwd) ? null : cwd.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedCwd))
            {
                var existingCwd = _sessionManager.GetSession(conversationId)?.Cwd?.Trim();
                if (!string.Equals(existingCwd, normalizedCwd, StringComparison.Ordinal)
                    && _sessionManager.UpdateSession(conversationId, session => session.Cwd = normalizedCwd, updateActivity: false))
                {
                    metadataChanged = true;
                }
            }

            if (updatedAtUtc is DateTime parsedUpdatedAt
                && parsedUpdatedAt != default
                && parsedUpdatedAt > binding.LastUpdatedAt)
            {
                binding.LastUpdatedAt = parsedUpdatedAt;
                metadataChanged = true;
            }

            if (metadataChanged)
            {
                NotifyConversationListChanged();
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
        var document = new ConversationDocument
        {
            Version = 4,
            LastActiveConversationId = null
        };

        document.DeletedConversationIds.AddRange(
            _deletedConversationTombstones
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal));

        foreach (var binding in _conversationBindings.Values.OrderByDescending(item => item.LastUpdatedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessionManager.GetSession(binding.ConversationId);
            var record = new ConversationRecord
            {
                ConversationId = binding.ConversationId,
                DisplayName = ResolveSessionDisplayName(binding.ConversationId),
                CreatedAt = binding.CreatedAt,
                LastUpdatedAt = binding.LastUpdatedAt,
                LastAccessedAt = binding.LastAccessedAt == default ? binding.LastUpdatedAt : binding.LastAccessedAt,
                Cwd = session?.Cwd,
                RemoteSessionId = binding.RemoteSessionId,
                BoundProfileId = binding.BoundProfileId,
                ProjectAffinityOverrideProjectId = binding.ProjectAffinityOverride?.ProjectId,
                SelectedModeId = binding.SelectedModeId,
                ShowConfigOptionsPanel = binding.ShowConfigOptionsPanel,
                ShowPlanPanel = binding.ShowPlanPanel,
                PlanTitle = binding.PlanTitle
            };

            record.Messages.AddRange(CloneMessages(binding.Transcript));
            record.AvailableModes.AddRange(binding.AvailableModes.Select(CloneModeOption));
            record.ConfigOptions.AddRange(binding.ConfigOptions.Select(CloneConfigOption));
            record.Plan.AddRange(binding.Plan.Select(ClonePlanEntry));

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

            var binding = RegisterConversation(
                conversation.ConversationId,
                conversation.CreatedAt,
                conversation.LastUpdatedAt,
                bumpVersion: false,
                clearTombstone: true);
            binding.LastAccessedAt = conversation.LastAccessedAt == default
                ? binding.LastUpdatedAt
                : conversation.LastAccessedAt;
            binding.Transcript.Clear();
            binding.Transcript.AddRange(CloneMessages(conversation.Messages));
            binding.Plan.Clear();
            binding.AvailableModes.Clear();
            binding.AvailableModes.AddRange((conversation.AvailableModes ?? []).Select(CloneModeOption));
            binding.SelectedModeId = conversation.SelectedModeId;
            binding.ConfigOptions.Clear();
            binding.ConfigOptions.AddRange((conversation.ConfigOptions ?? []).Select(CloneConfigOption));
            binding.ShowConfigOptionsPanel = conversation.ShowConfigOptionsPanel;
            binding.Plan.Clear();
            binding.Plan.AddRange((conversation.Plan ?? []).Select(ClonePlanEntry));
            binding.ShowPlanPanel = conversation.ShowPlanPanel;
            binding.PlanTitle = conversation.PlanTitle;
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

    private void RemoveConversation(string conversationId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (string.Equals(LastActiveConversationId, conversationId, StringComparison.Ordinal))
        {
            LastActiveConversationId = null;
        }

        _conversationBindings.Remove(conversationId);
        _deletedConversationTombstones.Add(conversationId);
        _sessionManager.RemoveSession(conversationId);
        ScheduleSave();
        NotifyConversationListChanged();
    }

    private void NotifyConversationListChanged()
    {
        ConversationListVersion++;
    }

    private void UpdateLastAccessedAt(string conversationId, DateTime accessedAt)
    {
        if (!_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            return;
        }

        binding.LastAccessedAt = accessedAt;
        ScheduleSave();
    }

    private ConversationBinding RegisterConversation(
        string conversationId,
        DateTime createdAt,
        DateTime lastUpdatedAt,
        bool bumpVersion,
        bool clearTombstone = false)
    {
        var existed = _conversationBindings.ContainsKey(conversationId);
        var binding = GetOrCreateConversationBinding(conversationId);
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

        if (bumpVersion && (!existed || actualLastUpdated != previousLastUpdated))
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

    private ConversationBinding GetOrCreateConversationBinding(string conversationId)
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
            ToolCallId = source.ToolCallId,
            ToolCallKind = source.ToolCallKind,
            ToolCallStatus = source.ToolCallStatus,
            ToolCallJson = source.ToolCallJson,
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

    private Task PostToContextAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SynchronizationContext.Current == _syncContext)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);

        return tcs.Task;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

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
    bool ShowConfigOptionsPanel = false);

public sealed record ConversationRemoteBindingState(
    string ConversationId,
    string? RemoteSessionId,
    string? BoundProfileId);

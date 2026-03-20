using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatConversationWorkspace : ObservableObject, IConversationCatalog, IConversationBindingCommands, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
    private readonly IConversationWorkspacePreferences _preferences;
    private readonly ILogger<ChatConversationWorkspace> _logger;
    private readonly SynchronizationContext _syncContext;
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private readonly Dictionary<string, ConversationBinding> _conversationBindings = new(StringComparer.Ordinal);
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
        SynchronizationContext? syncContext = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
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

    public string? CurrentConversationId
    {
        get => null;
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

    public IReadOnlyList<ConversationCatalogItem> GetCatalog()
        => _conversationBindings.Values
            .OrderByDescending(binding => binding.LastUpdatedAt)
            .Select(binding => new ConversationCatalogItem(
                binding.ConversationId,
                ResolveSessionDisplayName(binding.ConversationId),
                _sessionManager.GetSession(binding.ConversationId)?.Cwd,
                binding.CreatedAt,
                binding.LastUpdatedAt))
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
            await PostToContextAsync(() => LastActiveConversationId = sessionId, cancellationToken).ConfigureAwait(false);

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
            binding.Transcript.Select(CloneMessage).ToArray(),
            binding.Plan.Select(ClonePlanEntry).ToArray(),
            binding.ShowPlanPanel,
            binding.PlanTitle,
            binding.CreatedAt,
            binding.LastUpdatedAt);
    }

    public ConversationRemoteBindingState? GetRemoteBinding(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversationBindings.TryGetValue(conversationId, out var binding))
        {
            return null;
        }

        return new ConversationRemoteBindingState(binding.ConversationId, binding.RemoteSessionId, binding.BoundProfileId);
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

        var binding = GetOrCreateConversationBinding(snapshot.ConversationId);
        binding.CreatedAt = snapshot.CreatedAt == default ? binding.CreatedAt : snapshot.CreatedAt;
        binding.LastUpdatedAt = snapshot.LastUpdatedAt == default ? DateTime.UtcNow : snapshot.LastUpdatedAt;
        binding.Transcript.Clear();
        binding.Transcript.AddRange(snapshot.Transcript.Select(CloneMessage));
        binding.Plan.Clear();
        binding.Plan.AddRange(snapshot.Plan.Select(ClonePlanEntry));
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

        var binding = GetOrCreateConversationBinding(conversationId);
        binding.RemoteSessionId = remoteSessionId;
        binding.BoundProfileId = boundProfileId;
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
            Version = 1,
            LastActiveConversationId = null
        };

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
                Cwd = session?.Cwd
            };

            foreach (var message in binding.Transcript)
            {
                record.Messages.Add(CloneMessage(message));
            }

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
        foreach (var conversation in document.Conversations)
        {
            if (string.IsNullOrWhiteSpace(conversation.ConversationId))
            {
                continue;
            }

            var binding = GetOrCreateConversationBinding(conversation.ConversationId);
            binding.CreatedAt = conversation.CreatedAt == default ? DateTime.UtcNow : conversation.CreatedAt;
            binding.LastUpdatedAt = conversation.LastUpdatedAt == default ? DateTime.UtcNow : conversation.LastUpdatedAt;
            binding.Transcript.Clear();
            binding.Transcript.AddRange(conversation.Messages.Select(CloneMessage));
            binding.Plan.Clear();
            binding.ShowPlanPanel = false;
            binding.PlanTitle = null;

            var displayName = string.IsNullOrWhiteSpace(conversation.DisplayName)
                ? SessionNamePolicy.CreateDefault(conversation.ConversationId)
                : conversation.DisplayName.Trim();

            _sessionManager.UpdateSession(
                conversation.ConversationId,
                session =>
                {
                    session.DisplayName = displayName;
                    session.CreatedAt = binding.CreatedAt;
                    session.LastActivityAt = binding.LastUpdatedAt;
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
            .OrderByDescending(binding => binding.LastUpdatedAt)
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
        _sessionManager.RemoveSession(conversationId);
        ScheduleSave();
        NotifyConversationListChanged();
    }

    private void NotifyConversationListChanged()
    {
        ConversationListVersion++;
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

    private static ConversationPlanEntrySnapshot ClonePlanEntry(ConversationPlanEntrySnapshot source)
        => new()
        {
            Content = source.Content,
            Status = source.Status,
            Priority = source.Priority
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
        }

        public string ConversationId { get; }

        public string? BoundProfileId { get; set; }

        public string? RemoteSessionId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime LastUpdatedAt { get; set; }

        public List<ConversationMessageSnapshot> Transcript { get; } = new();

        public List<ConversationPlanEntrySnapshot> Plan { get; } = new();

        public bool ShowPlanPanel { get; set; }

        public string? PlanTitle { get; set; }
    }
}

public sealed record ConversationWorkspaceSnapshot(
    string ConversationId,
    IReadOnlyList<ConversationMessageSnapshot> Transcript,
    IReadOnlyList<ConversationPlanEntrySnapshot> Plan,
    bool ShowPlanPanel,
    string? PlanTitle,
    DateTime CreatedAt,
    DateTime LastUpdatedAt);

public sealed record ConversationRemoteBindingState(
    string ConversationId,
    string? RemoteSessionId,
    string? BoundProfileId);

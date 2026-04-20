using System;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using System.Text.Json;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IWorkspaceWriter
{
    void Enqueue(ChatState state, bool scheduleSave);

    Task FlushAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkspaceWriter : IWorkspaceWriter, IDisposable
{
    private const int DefaultThrottleMilliseconds = 500;

    private readonly ChatConversationWorkspace _workspace;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeSpan _throttleWindow;
    private CancellationTokenSource? _flushCts;
    private PendingWrite? _pending;
    private DateTime _lastFlushAt;
    private bool _disposed;

    public WorkspaceWriter(
        ChatConversationWorkspace workspace,
        IUiDispatcher uiDispatcher,
        TimeSpan? throttleWindow = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _throttleWindow = throttleWindow ?? TimeSpan.FromMilliseconds(DefaultThrottleMilliseconds);
        _lastFlushAt = DateTime.MinValue;
    }

    public void Enqueue(ChatState state, bool scheduleSave)
    {
        ThrowIfDisposed();
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var pending = CreatePendingWrite(state, scheduleSave);
        if (pending is null)
        {
            return;
        }

        if (_pending?.ScheduleSave == true)
        {
            pending.ScheduleSave = true;
        }

        _pending = pending;

        var delay = ComputeDelay();
        if (delay <= TimeSpan.Zero)
        {
            _ = FlushAsync();
            return;
        }

        ScheduleDelayedFlush(delay);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var pending = _pending;
        if (pending is null)
        {
            return;
        }

        _pending = null;
        CancelScheduledFlush();
        _lastFlushAt = DateTime.UtcNow;

        await PostToContextAsync(() =>
        {
            foreach (var snapshot in pending.Snapshots)
            {
                _workspace.UpsertConversationSnapshot(snapshot);
            }

            if (pending.ScheduleSave)
            {
                _workspace.ScheduleSave();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending = null;
        CancelScheduledFlush();
    }

    private PendingWrite? CreatePendingWrite(ChatState state, bool scheduleSave)
    {
        var conversationIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(state.HydratedConversationId))
        {
            conversationIds.Add(state.HydratedConversationId!);
        }

        if (state.ConversationContents != null)
        {
            foreach (var key in state.ConversationContents.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    conversationIds.Add(key);
                }
            }
        }

        if (state.ConversationSessionStates != null)
        {
            foreach (var key in state.ConversationSessionStates.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    conversationIds.Add(key);
                }
            }
        }

        var snapshots = conversationIds
            .Select(conversationId => CreateSnapshotForConversation(state, conversationId))
            .OfType<ConversationWorkspaceSnapshot>()
            .ToArray();
        if (snapshots.Length == 0)
        {
            return null;
        }

        return new PendingWrite(snapshots, scheduleSave);
    }

    private ConversationWorkspaceSnapshot? CreateSnapshotForConversation(ChatState state, string conversationId)
    {
        var contentSlice = state.ResolveContentSlice(conversationId);
        var sessionStateSlice = state.ResolveSessionStateSlice(conversationId);
        var isHydratedConversation = string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal);
        var existingSnapshot = _workspace.GetConversationSnapshot(conversationId);
        if (contentSlice is null
            && sessionStateSlice is null
            && (!isHydratedConversation || (state.Transcript is null && state.PlanEntries is null)))
        {
            return null;
        }

        var hasProjectedContent = contentSlice is not null
            || (isHydratedConversation && HasProjectedConversationContent(state.Transcript, state.PlanEntries, state.ShowPlanPanel, state.PlanTitle));
        var transcriptSource = hasProjectedContent
            ? contentSlice?.Transcript ?? (isHydratedConversation ? state.Transcript : null)
            : existingSnapshot?.Transcript;
        var planEntriesSource = hasProjectedContent
            ? contentSlice?.PlanEntries ?? (isHydratedConversation ? state.PlanEntries : null)
            : existingSnapshot?.Plan;
        var transcript = (transcriptSource ?? ImmutableList<ConversationMessageSnapshot>.Empty)
            .Where(static message => !IsThinkingPlaceholder(message))
            .Select(CloneMessageSnapshot)
            .ToArray();
        var planEntries = (planEntriesSource ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty)
            .Select(ClonePlanEntrySnapshot)
            .OfType<ConversationPlanEntrySnapshot>()
            .ToArray();
        ConversationModeOptionSnapshot[] availableModes = (sessionStateSlice?.AvailableModes ?? (isHydratedConversation ? state.AvailableModes : null) ?? ImmutableList<ConversationModeOptionSnapshot>.Empty)
            .Select(CloneModeOptionSnapshot)
            .ToArray();
        ConversationConfigOptionSnapshot[] configOptions = (sessionStateSlice?.ConfigOptions ?? (isHydratedConversation ? state.ConfigOptions : null) ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty)
            .Select(CloneConfigOptionSnapshot)
            .ToArray();
        ConversationAvailableCommandSnapshot[] availableCommands = (sessionStateSlice?.AvailableCommands ?? (isHydratedConversation ? state.AvailableCommands : null) ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty)
            .Select(CloneAvailableCommandSnapshot)
            .ToArray();
        string? selectedModeId = sessionStateSlice?.SelectedModeId ?? (isHydratedConversation ? state.SelectedModeId : null);
        bool showConfigOptionsPanel = sessionStateSlice?.ShowConfigOptionsPanel ?? (isHydratedConversation && state.ShowConfigOptionsPanel);
        ConversationSessionInfoSnapshot? sessionInfo = ConversationSessionInfoSnapshots.Clone(sessionStateSlice?.SessionInfo ?? (isHydratedConversation ? state.SessionInfo : null));
        ConversationUsageSnapshot? usage = CloneUsageSnapshot(sessionStateSlice?.Usage ?? (isHydratedConversation ? state.Usage : null));
        if (sessionStateSlice is null && !isHydratedConversation && existingSnapshot is not null)
        {
            availableModes = (existingSnapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>())
                .Select(CloneModeOptionSnapshot)
                .ToArray();
            configOptions = (existingSnapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>())
                .Select(CloneConfigOptionSnapshot)
                .ToArray();
            availableCommands = (existingSnapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>())
                .Select(CloneAvailableCommandSnapshot)
                .ToArray();
            selectedModeId = existingSnapshot.SelectedModeId;
            showConfigOptionsPanel = existingSnapshot.ShowConfigOptionsPanel;
            sessionInfo = ConversationSessionInfoSnapshots.Clone(existingSnapshot.SessionInfo);
            usage = CloneUsageSnapshot(existingSnapshot.Usage);
        }
        var showPlanPanel = hasProjectedContent
            ? contentSlice?.ShowPlanPanel ?? (isHydratedConversation && state.ShowPlanPanel)
            : existingSnapshot?.ShowPlanPanel ?? false;
        var planTitle = hasProjectedContent
            ? contentSlice?.PlanTitle ?? (isHydratedConversation ? state.PlanTitle : null)
            : existingSnapshot?.PlanTitle;
        var runtimeState = state.ResolveRuntimeState(conversationId);
        var hasProjectedData = HasProjectedData(
            transcript,
            planEntries,
            availableModes,
            selectedModeId,
            configOptions,
            showConfigOptionsPanel,
            availableCommands,
            sessionInfo,
            usage,
            showPlanPanel,
            planTitle);
        if (!hasProjectedData
            && existingSnapshot is not null
            && HasSnapshotData(existingSnapshot)
            && runtimeState?.Phase is not ConversationRuntimePhase.Warm)
        {
            return null;
        }

        var lastUpdatedAt = existingSnapshot != null
            && SnapshotContentMatches(
                existingSnapshot,
                transcript,
                planEntries,
                availableModes,
                selectedModeId,
                configOptions,
                showConfigOptionsPanel,
                availableCommands,
                sessionInfo,
                usage,
                showPlanPanel,
                planTitle)
            ? existingSnapshot.LastUpdatedAt
            : DateTime.UtcNow;

        return new ConversationWorkspaceSnapshot(
            conversationId,
            transcript,
            planEntries,
            showPlanPanel,
            planTitle,
            default,
            lastUpdatedAt,
            availableModes,
            selectedModeId,
            configOptions,
            showConfigOptionsPanel,
            availableCommands,
            sessionInfo,
            usage);
    }

    private static bool HasSnapshotData(ConversationWorkspaceSnapshot snapshot)
        => HasProjectedData(
            snapshot.Transcript,
            snapshot.Plan,
            snapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>(),
            snapshot.SelectedModeId,
            snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>(),
            snapshot.ShowConfigOptionsPanel,
            snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>(),
            snapshot.SessionInfo,
            snapshot.Usage,
            snapshot.ShowPlanPanel,
            snapshot.PlanTitle);

    private static bool HasProjectedConversationContent(
        IReadOnlyList<ConversationMessageSnapshot>? transcript,
        IReadOnlyList<ConversationPlanEntrySnapshot>? planEntries,
        bool showPlanPanel,
        string? planTitle)
        => (transcript?.Count ?? 0) > 0
            || (planEntries?.Count ?? 0) > 0
            || showPlanPanel
            || !string.IsNullOrWhiteSpace(planTitle);

    private static bool HasProjectedData(
        IReadOnlyList<ConversationMessageSnapshot> transcript,
        IReadOnlyList<ConversationPlanEntrySnapshot> planEntries,
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel,
        IReadOnlyList<ConversationAvailableCommandSnapshot> availableCommands,
        ConversationSessionInfoSnapshot? sessionInfo,
        ConversationUsageSnapshot? usage,
        bool showPlanPanel,
        string? planTitle)
        => transcript.Count > 0
            || planEntries.Count > 0
            || availableModes.Count > 0
            || configOptions.Count > 0
            || availableCommands.Count > 0
            || showConfigOptionsPanel
            || showPlanPanel
            || !string.IsNullOrWhiteSpace(selectedModeId)
            || sessionInfo is not null
            || usage is not null
            || !string.IsNullOrWhiteSpace(planTitle);

    private TimeSpan ComputeDelay()
    {
        if (_lastFlushAt == DateTime.MinValue)
        {
            return TimeSpan.Zero;
        }

        var elapsed = DateTime.UtcNow - _lastFlushAt;
        var remaining = _throttleWindow - elapsed;
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private void ScheduleDelayedFlush(TimeSpan delay)
    {
        CancelScheduledFlush();

        _flushCts = new CancellationTokenSource();
        var token = _flushCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                await FlushAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }, token);
    }

    private void CancelScheduledFlush()
    {
        _flushCts?.Cancel();
        _flushCts?.Dispose();
        _flushCts = null;
    }

    private Task PostToContextAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiDispatcher.Enqueue(() =>
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
        });

        return tcs.Task;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsThinkingPlaceholder(ConversationMessageSnapshot message)
        => string.Equals(message.ContentType, "thinking", StringComparison.OrdinalIgnoreCase);

    private static bool SnapshotContentMatches(
        ConversationWorkspaceSnapshot existingSnapshot,
        IReadOnlyList<ConversationMessageSnapshot> transcript,
        IReadOnlyList<ConversationPlanEntrySnapshot> planEntries,
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel,
        IReadOnlyList<ConversationAvailableCommandSnapshot> availableCommands,
        ConversationSessionInfoSnapshot? sessionInfo,
        ConversationUsageSnapshot? usage,
        bool showPlanPanel,
        string? planTitle)
    {
        return existingSnapshot.ShowConfigOptionsPanel == showConfigOptionsPanel
            && string.Equals(existingSnapshot.SelectedModeId, selectedModeId, StringComparison.Ordinal)
            && existingSnapshot.ShowPlanPanel == showPlanPanel
            && string.Equals(existingSnapshot.PlanTitle, planTitle, StringComparison.Ordinal)
            && ModeSequencesEqual(existingSnapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>(), availableModes)
            && ConfigOptionSequencesEqual(existingSnapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>(), configOptions)
            && AvailableCommandSequencesEqual(existingSnapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>(), availableCommands)
            && SessionInfoEquals(existingSnapshot.SessionInfo, sessionInfo)
            && UsageEquals(existingSnapshot.Usage, usage)
            && MessageSequencesEqual(existingSnapshot.Transcript, transcript)
            && PlanSequencesEqual(existingSnapshot.Plan, planEntries);
    }

    private static bool ModeSequencesEqual(
        IReadOnlyList<ConversationModeOptionSnapshot> left,
        IReadOnlyList<ConversationModeOptionSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ModeOptionEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigOptionSequencesEqual(
        IReadOnlyList<ConversationConfigOptionSnapshot> left,
        IReadOnlyList<ConversationConfigOptionSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ConfigOptionEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AvailableCommandSequencesEqual(
        IReadOnlyList<ConversationAvailableCommandSnapshot> left,
        IReadOnlyList<ConversationAvailableCommandSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!AvailableCommandEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MessageSequencesEqual(
        IReadOnlyList<ConversationMessageSnapshot> left,
        IReadOnlyList<ConversationMessageSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!MessageEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PlanSequencesEqual(
        IReadOnlyList<ConversationPlanEntrySnapshot> left,
        IReadOnlyList<ConversationPlanEntrySnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!PlanEntryEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MessageEquals(ConversationMessageSnapshot left, ConversationMessageSnapshot right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Timestamp == right.Timestamp
            && left.IsOutgoing == right.IsOutgoing
            && string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
            && string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            && string.Equals(left.TextContent, right.TextContent, StringComparison.Ordinal)
            && string.Equals(left.ImageData, right.ImageData, StringComparison.Ordinal)
            && string.Equals(left.ImageMimeType, right.ImageMimeType, StringComparison.Ordinal)
            && string.Equals(left.AudioData, right.AudioData, StringComparison.Ordinal)
            && string.Equals(left.AudioMimeType, right.AudioMimeType, StringComparison.Ordinal)
            && string.Equals(left.ToolCallId, right.ToolCallId, StringComparison.Ordinal)
            && left.ToolCallKind == right.ToolCallKind
            && left.ToolCallStatus == right.ToolCallStatus
            && string.Equals(left.ToolCallJson, right.ToolCallJson, StringComparison.Ordinal)
            && ToolCallContentEquals(left.ToolCallContent, right.ToolCallContent)
            && string.Equals(left.ModeId, right.ModeId, StringComparison.Ordinal)
            && PlanEntryEquals(left.PlanEntry, right.PlanEntry);
    }

    private static bool ToolCallContentEquals(IReadOnlyList<ToolCallContent>? left, IReadOnlyList<ToolCallContent>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(JsonSerializer.Serialize(left[i]), JsonSerializer.Serialize(right[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PlanEntryEquals(ConversationPlanEntrySnapshot? left, ConversationPlanEntrySnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Content, right.Content, StringComparison.Ordinal)
            && left.Status == right.Status
            && left.Priority == right.Priority;
    }

    private static bool ModeOptionEquals(ConversationModeOptionSnapshot left, ConversationModeOptionSnapshot right)
    {
        return string.Equals(left.ModeId, right.ModeId, StringComparison.Ordinal)
            && string.Equals(left.ModeName, right.ModeName, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal);
    }

    private static bool ConfigOptionEquals(ConversationConfigOptionSnapshot left, ConversationConfigOptionSnapshot right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && string.Equals(left.ValueType, right.ValueType, StringComparison.Ordinal)
            && string.Equals(left.SelectedValue, right.SelectedValue, StringComparison.Ordinal)
            && ConfigOptionChoiceSequencesEqual(left.Options ?? [], right.Options ?? []);
    }

    private static bool AvailableCommandEquals(ConversationAvailableCommandSnapshot left, ConversationAvailableCommandSnapshot right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            && string.Equals(left.InputHint, right.InputHint, StringComparison.Ordinal);
    }

    private static bool SessionInfoEquals(ConversationSessionInfoSnapshot? left, ConversationSessionInfoSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            && string.Equals(left.Cwd, right.Cwd, StringComparison.Ordinal)
            && left.UpdatedAtUtc == right.UpdatedAtUtc
            && MetadataEquals(left.Meta, right.Meta);
    }

    private static bool UsageEquals(ConversationUsageSnapshot? left, ConversationUsageSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Used == right.Used
            && left.Size == right.Size
            && UsageCostEquals(left.Cost, right.Cost);
    }

    private static bool UsageCostEquals(ConversationUsageCostSnapshot? left, ConversationUsageCostSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Amount == right.Amount
            && string.Equals(left.Currency, right.Currency, StringComparison.Ordinal);
    }

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, object?>? left,
        IReadOnlyDictionary<string, object?>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var rightValue))
            {
                return false;
            }

            if (!Equals(pair.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigOptionChoiceSequencesEqual(
        IReadOnlyList<ConversationConfigOptionChoiceSnapshot> left,
        IReadOnlyList<ConversationConfigOptionChoiceSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ConfigOptionChoiceEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigOptionChoiceEquals(ConversationConfigOptionChoiceSnapshot left, ConversationConfigOptionChoiceSnapshot right)
    {
        return string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal);
    }

    private static ConversationMessageSnapshot CloneMessageSnapshot(ConversationMessageSnapshot snapshot)
        => new()
        {
            Id = snapshot.Id,
            Timestamp = snapshot.Timestamp,
            IsOutgoing = snapshot.IsOutgoing,
            ContentType = snapshot.ContentType,
            Title = snapshot.Title,
            TextContent = snapshot.TextContent,
            ImageData = snapshot.ImageData,
            ImageMimeType = snapshot.ImageMimeType,
            AudioData = snapshot.AudioData,
            AudioMimeType = snapshot.AudioMimeType,
            ToolCallId = snapshot.ToolCallId,
            ToolCallKind = snapshot.ToolCallKind,
            ToolCallStatus = snapshot.ToolCallStatus,
            ToolCallJson = snapshot.ToolCallJson,
            ToolCallContent = CloneToolCallContentList(snapshot.ToolCallContent),
            PlanEntry = ClonePlanEntrySnapshot(snapshot.PlanEntry),
            ModeId = snapshot.ModeId
        };

    private static List<ToolCallContent>? CloneToolCallContentList(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is null)
        {
            return null;
        }

        var cloned = new List<ToolCallContent>(content.Count);
        foreach (var item in content)
        {
            var json = JsonSerializer.Serialize(item);
            cloned.Add(JsonSerializer.Deserialize<ToolCallContent>(json)
                ?? throw new InvalidOperationException("Failed to clone tool call content."));
        }

        return cloned;
    }

    private static ConversationPlanEntrySnapshot? ClonePlanEntrySnapshot(ConversationPlanEntrySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new ConversationPlanEntrySnapshot
        {
            Content = snapshot.Content,
            Status = snapshot.Status,
            Priority = snapshot.Priority
        };
    }

    private static ConversationModeOptionSnapshot CloneModeOptionSnapshot(ConversationModeOptionSnapshot snapshot)
        => new()
        {
            ModeId = snapshot.ModeId,
            ModeName = snapshot.ModeName,
            Description = snapshot.Description
        };

    private static ConversationConfigOptionSnapshot CloneConfigOptionSnapshot(ConversationConfigOptionSnapshot snapshot)
        => new()
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Description = snapshot.Description,
            Category = snapshot.Category,
            ValueType = snapshot.ValueType,
            SelectedValue = snapshot.SelectedValue,
            Options = (snapshot.Options ?? [])
                .Select(CloneConfigOptionChoiceSnapshot)
                .ToList()
        };

    private static ConversationConfigOptionChoiceSnapshot CloneConfigOptionChoiceSnapshot(ConversationConfigOptionChoiceSnapshot snapshot)
        => new()
        {
            Value = snapshot.Value,
            Name = snapshot.Name,
            Description = snapshot.Description
        };

    private static ConversationAvailableCommandSnapshot CloneAvailableCommandSnapshot(ConversationAvailableCommandSnapshot snapshot)
        => new(snapshot.Name, snapshot.Description, snapshot.InputHint);

    private static ConversationUsageSnapshot? CloneUsageSnapshot(ConversationUsageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new ConversationUsageSnapshot(
            snapshot.Used,
            snapshot.Size,
            snapshot.Cost is null
                ? null
                : new ConversationUsageCostSnapshot(snapshot.Cost.Amount, snapshot.Cost.Currency));
    }

    private sealed class PendingWrite
    {
        public PendingWrite(IReadOnlyList<ConversationWorkspaceSnapshot> snapshots, bool scheduleSave)
        {
            Snapshots = snapshots;
            ScheduleSave = scheduleSave;
        }

        public IReadOnlyList<ConversationWorkspaceSnapshot> Snapshots { get; }

        public bool ScheduleSave { get; set; }
    }
}

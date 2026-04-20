using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public readonly record struct ConversationContentSlice(
    IImmutableList<ConversationMessageSnapshot> Transcript,
    IImmutableList<ConversationPlanEntrySnapshot> PlanEntries,
    bool ShowPlanPanel,
    string? PlanTitle);

public readonly record struct ConversationSessionStateSlice(
    IImmutableList<ConversationModeOptionSnapshot> AvailableModes,
    string? SelectedModeId,
    IImmutableList<ConversationConfigOptionSnapshot> ConfigOptions,
    bool ShowConfigOptionsPanel,
    IImmutableList<ConversationAvailableCommandSnapshot> AvailableCommands,
    ConversationSessionInfoSnapshot? SessionInfo,
    ConversationUsageSnapshot? Usage);

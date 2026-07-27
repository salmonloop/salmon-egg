using System;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

/// <summary>
/// Presentation projections between Domain plan wire strings and ACP plan value types.
/// </summary>
internal static class ConversationPlanWire
{
    public static PlanEntryStatus ParseStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? PlanEntryStatus.Pending : new PlanEntryStatus(value);

    public static PlanEntryPriority ParsePriority(string? value)
        => string.IsNullOrWhiteSpace(value) ? PlanEntryPriority.Low : new PlanEntryPriority(value);

    public static string FormatStatus(PlanEntryStatus status) => status.ToString();

    public static string FormatPriority(PlanEntryPriority priority) => priority.ToString();

    public static ConversationPlanEntrySnapshot ToDomain(PlanEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ConversationPlanEntrySnapshot
        {
            Content = entry.Content ?? string.Empty,
            Status = FormatStatus(entry.Status),
            Priority = FormatPriority(entry.Priority)
        };
    }

    public static ConversationPlanEntrySnapshot CloneDomain(ConversationPlanEntrySnapshot snapshot)
        => new()
        {
            Content = snapshot.Content ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(snapshot.Status) ? "pending" : snapshot.Status,
            Priority = string.IsNullOrWhiteSpace(snapshot.Priority) ? "low" : snapshot.Priority
        };
}

using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Models.Plan;

namespace SalmonEgg.Domain.Models.Conversation
{
    public sealed class ConversationDocument
    {
        public int Version { get; set; } = 1;

        public string? LastActiveConversationId { get; set; }

        public List<ConversationRecord> Conversations { get; set; } = new();
    }

    public sealed class ConversationRecord
    {
        public string ConversationId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public List<ConversationMessageSnapshot> Messages { get; set; } = new();
    }

    public sealed class ConversationMessageSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool IsOutgoing { get; set; }

        public string ContentType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string TextContent { get; set; } = string.Empty;

        public string ImageData { get; set; } = string.Empty;

        public string ImageMimeType { get; set; } = string.Empty;

        public string AudioData { get; set; } = string.Empty;

        public string AudioMimeType { get; set; } = string.Empty;

        public string? ToolCallId { get; set; }

        public ToolCallKind? ToolCallKind { get; set; }

        public ToolCallStatus? ToolCallStatus { get; set; }

        public string? ToolCallJson { get; set; }

        public ConversationPlanEntrySnapshot? PlanEntry { get; set; }

        public string? ModeId { get; set; }
    }

    public sealed class ConversationPlanEntrySnapshot
    {
        public string Content { get; set; } = string.Empty;

        public PlanEntryStatus Status { get; set; }

        public PlanEntryPriority Priority { get; set; }
    }
}


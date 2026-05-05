using System.Collections.Generic;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Tool;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Source-generated JsonSerializerContext for all conversation persistence types.
/// Every type serialized transitively through ConversationDocument must be registered
/// here so that trimming/AOT metadata is available at compile time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConversationDocument))]
[JsonSerializable(typeof(ConversationRecord))]
[JsonSerializable(typeof(ConversationMessageSnapshot))]
[JsonSerializable(typeof(ConversationModeOptionSnapshot))]
[JsonSerializable(typeof(ConversationConfigOptionSnapshot))]
[JsonSerializable(typeof(ConversationConfigOptionChoiceSnapshot))]
[JsonSerializable(typeof(ConversationSessionInfoSnapshot))]
[JsonSerializable(typeof(ConversationAvailableCommandSnapshot))]
[JsonSerializable(typeof(ConversationUsageSnapshot))]
[JsonSerializable(typeof(ConversationUsageCostSnapshot))]
[JsonSerializable(typeof(ConversationPlanEntrySnapshot))]

// ToolCallContent polymorphic hierarchy (referenced by ConversationMessageSnapshot.ToolCallContent)
[JsonSerializable(typeof(ToolCallContent))]
[JsonSerializable(typeof(ContentToolCallContent))]
[JsonSerializable(typeof(DiffToolCallContent))]
[JsonSerializable(typeof(TerminalToolCallContent))]

// ContentBlock has a custom [JsonConverter], so source-gen can't provide metadata.
// It is handled at runtime via ContentBlockJsonConverter on the type itself.
// Its subclasses and Annotations/EmbeddedResource ARE source-gen compatible.
[JsonSerializable(typeof(TextContentBlock))]
[JsonSerializable(typeof(ImageContentBlock))]
[JsonSerializable(typeof(AudioContentBlock))]
[JsonSerializable(typeof(ResourceContentBlock))]
[JsonSerializable(typeof(ResourceLinkContentBlock))]
[JsonSerializable(typeof(Annotations))]
[JsonSerializable(typeof(EmbeddedResource))]

// Enums with custom [JsonConverter] (ToolCallKind, ToolCallStatus) are handled
// at runtime via their converter attributes — not source-gen compatible.
// PlanEntryStatus and PlanEntryPriority use [JsonPropertyName] and ARE compatible.
[JsonSerializable(typeof(PlanEntryStatus))]
[JsonSerializable(typeof(PlanEntryPriority))]

// Session meta dictionary — object? is handled by built-in converter
[JsonSerializable(typeof(Dictionary<string, object?>))]
internal partial class ConversationJsonContext : JsonSerializerContext
{
}

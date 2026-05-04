using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Infrastructure.Storage;

// Types with custom [JsonConverter] (ContentBlock, ToolCallKind, ToolCallStatus)
// are handled by the DefaultJsonTypeInfoResolver fallback at runtime;
// the source generator warnings for these are expected and benign.
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
internal partial class ConversationJsonContext : JsonSerializerContext
{
}
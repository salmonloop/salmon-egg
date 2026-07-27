using System.Collections.Generic;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Source-generated JsonSerializerContext for conversation persistence types.
/// ACP wire values nested under conversation snapshots are handled by Domain-owned
/// property converters that route through <c>AcpJsonContext</c>, so this context must
/// not re-register ACP types or depend on internal ACP converters.
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
[JsonSerializable(typeof(Dictionary<string, object?>))]
internal partial class ConversationJsonContext : JsonSerializerContext
{
}

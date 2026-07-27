using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Acp.Plan;

namespace SalmonEgg.Domain.Models.Conversation
{
    public sealed class ConversationDocument
    {
        public int Version { get; set; } = 2;

        public string? LastActiveConversationId { get; set; }

        public List<ConversationRecord> Conversations { get; set; } = new();

        public List<string> DeletedConversationIds { get; set; } = new();
    }

    public sealed class ConversationRecord
    {
        public string ConversationId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 会话的工作目录，用于会话重启后正确分类到对应项目。
        /// </summary>
        public string? Cwd { get; set; }

        public string? RemoteSessionId { get; set; }

        public string? BoundProfileId { get; set; }

        public string? ProjectAffinityOverrideProjectId { get; set; }

        public List<ConversationMessageSnapshot> Messages { get; set; } = new();

        public List<ConversationModeOptionSnapshot> AvailableModes { get; set; } = new();

        public string? SelectedModeId { get; set; }

        public List<ConversationConfigOptionSnapshot> ConfigOptions { get; set; } = new();

        public bool ShowConfigOptionsPanel { get; set; }

        public ConversationSessionInfoSnapshot? SessionInfo { get; set; }

        public List<ConversationAvailableCommandSnapshot> AvailableCommands { get; set; } = new();

        public ConversationUsageSnapshot? Usage { get; set; }

        public List<ConversationPlanEntrySnapshot> Plan { get; set; } = new();

        public bool ShowPlanPanel { get; set; }
    }

    public sealed class ConversationModeOptionSnapshot
    {
        public string ModeId { get; set; } = string.Empty;

        public string ModeName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }

    public sealed class ConversationConfigOptionSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Category { get; set; }

        public string? ValueType { get; set; }

        public string? SelectedValue { get; set; }

        public List<ConversationConfigOptionChoiceSnapshot> Options { get; set; } = new();
    }

    public sealed class ConversationConfigOptionChoiceSnapshot
    {
        public string Value { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
    }

    public sealed class ConversationSessionInfoSnapshot
    {
        private string? _title;
        private DateTime? _updatedAtUtc;

        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                HasTitle = true;
            }
        }

        public bool HasTitle { get; set; }

        public string? Cwd { get; set; }

        public List<string>? AdditionalDirectories { get; set; }

        public DateTime? UpdatedAtUtc
        {
            get => _updatedAtUtc;
            set
            {
                _updatedAtUtc = value;
                HasUpdatedAt = true;
            }
        }

        public bool HasUpdatedAt { get; set; }

        [JsonConverter(typeof(ConversationMetaDictionaryJsonConverter))]
        public Dictionary<string, object?>? Meta { get; set; }
    }

    public sealed class ConversationAvailableCommandSnapshot
    {
        public ConversationAvailableCommandSnapshot()
        {
        }

        public ConversationAvailableCommandSnapshot(string name, string description, string? inputHint)
        {
            Name = name;
            Description = description;
            InputHint = inputHint;
        }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? InputHint { get; set; }
    }

    public sealed class ConversationUsageSnapshot
    {
        public ConversationUsageSnapshot()
        {
        }

        public ConversationUsageSnapshot(ulong used, ulong size, ConversationUsageCostSnapshot? cost)
        {
            Used = used;
            Size = size;
            Cost = cost;
        }

        public ulong Used { get; set; }

        public ulong Size { get; set; }

        public ConversationUsageCostSnapshot? Cost { get; set; }
    }

    public sealed class ConversationUsageCostSnapshot
    {
        public ConversationUsageCostSnapshot()
        {
        }

        public ConversationUsageCostSnapshot(double amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public double Amount { get; set; }

        public string Currency { get; set; } = string.Empty;
    }

    public sealed class ConversationMessageSnapshot
    {
        public string Id { get; set; } = string.Empty;

        // null = no authoritative message time is available.
        // ACP session/load replay and per-chunk updates carry no message timestamp;
        // local user prompts carry an observed emit time. The absence of a value
        // must never be masked with a wall clock — the UI hides time when null.
        public DateTime? Timestamp { get; set; }

        public bool IsOutgoing { get; set; }

        public string ContentType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string TextContent { get; set; } = string.Empty;

        public string ImageData { get; set; } = string.Empty;

        public string ImageMimeType { get; set; } = string.Empty;

        public string AudioData { get; set; } = string.Empty;

        public string AudioMimeType { get; set; } = string.Empty;

        public string? ProtocolMessageId { get; set; }

        public string? ToolCallId { get; set; }

        /// <summary>
        /// In-memory ACP tool-call kind. Persistence uses <see cref="ToolCallKindWire"/>.
        /// </summary>
        [JsonIgnore]
        public ToolCallKind? ToolCallKind { get; set; }

        [JsonPropertyName("toolCallKind")]
        public string? ToolCallKindWire
        {
            get => ToolCallKind?.ToString();
            set => ToolCallKind = ConversationAcpWireProjection.ParseToolCallKind(value);
        }

        /// <summary>
        /// In-memory ACP tool-call status. Persistence uses <see cref="ToolCallStatusWire"/>.
        /// </summary>
        [JsonIgnore]
        public ToolCallStatus? ToolCallStatus { get; set; }

        [JsonPropertyName("toolCallStatus")]
        public string? ToolCallStatusWire
        {
            get => ToolCallStatus?.ToString();
            set => ToolCallStatus = ConversationAcpWireProjection.ParseToolCallStatus(value);
        }

        public string? ToolCallJson { get; set; }

        public string? ToolCallRawInputJson { get; set; }

        public string? ToolCallRawOutputJson { get; set; }

        /// <summary>
        /// In-memory ACP tool-call content blocks. Persistence uses <see cref="ToolCallContentWire"/>.
        /// </summary>
        [JsonIgnore]
        public List<ToolCallContent>? ToolCallContent { get; set; }

        [JsonPropertyName("toolCallContent")]
        public JsonElement? ToolCallContentWire
        {
            get => ConversationAcpWireProjection.SerializeToolCallContent(ToolCallContent);
            set => ToolCallContent = ConversationAcpWireProjection.DeserializeToolCallContent(value);
        }

        /// <summary>
        /// In-memory ACP tool-call locations. Persistence uses <see cref="ToolCallLocationsWire"/>.
        /// </summary>
        [JsonIgnore]
        public List<ToolCallLocation>? ToolCallLocations { get; set; }

        [JsonPropertyName("toolCallLocations")]
        public JsonElement? ToolCallLocationsWire
        {
            get => ConversationAcpWireProjection.SerializeToolCallLocations(ToolCallLocations);
            set => ToolCallLocations = ConversationAcpWireProjection.DeserializeToolCallLocations(value);
        }

        public ConversationPlanEntrySnapshot? PlanEntry { get; set; }

        public string? ModeId { get; set; }
    }

    public sealed class ConversationPlanEntrySnapshot
    {
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// In-memory ACP plan status. Persistence uses <see cref="StatusWire"/>.
        /// </summary>
        [JsonIgnore]
        public PlanEntryStatus Status { get; set; } = PlanEntryStatus.Pending;

        [JsonPropertyName("status")]
        public string StatusWire
        {
            get => Status.ToString();
            set => Status = ConversationAcpWireProjection.ParsePlanEntryStatus(value);
        }

        /// <summary>
        /// In-memory ACP plan priority. Persistence uses <see cref="PriorityWire"/>.
        /// </summary>
        [JsonIgnore]
        public PlanEntryPriority Priority { get; set; } = PlanEntryPriority.Low;

        [JsonPropertyName("priority")]
        public string PriorityWire
        {
            get => Priority.ToString();
            set => Priority = ConversationAcpWireProjection.ParsePlanEntryPriority(value);
        }
    }

    /// <summary>
    /// Domain-owned meta dictionary converter. Delegates lossless ACP _meta token rules to <see cref="AcpMetaJson"/>.
    /// Keeps the SDK JsonConverter implementation internal while preserving conversation document round-trip.
    /// </summary>
    public sealed class ConversationMetaDictionaryJsonConverter : JsonConverter<Dictionary<string, object?>?>
    {
        public override Dictionary<string, object?>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
            => AcpMetaJson.ReadValue(ref reader);

        public override void Write(
            Utf8JsonWriter writer,
            Dictionary<string, object?>? value,
            JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            AcpMetaJson.WriteObject(writer, value);
        }
    }
}

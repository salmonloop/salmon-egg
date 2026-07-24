using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Plan
{
    /// <summary>
    /// 计划类。
    /// 表示 Agent 的行动计划，包含一系列计划条目。
    /// </summary>
    public class Plan : AcpProtocolObject
    {
        private List<PlanEntry> _entries = new List<PlanEntry>();

        /// <summary>
        /// 计划条目列表。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("entries")]
        public List<PlanEntry> Entries
        {
            get => _entries;
            set => _entries = ValidateEntries(value);
        }

        /// <summary>
        /// 创建新的计划实例。
        /// </summary>
        public Plan()
        {
        }

        /// <summary>
        /// 创建新的计划实例。
        /// </summary>
        /// <param name="entries">计划条目列表</param>
        public Plan(List<PlanEntry> entries)
        {
            Entries = entries;
        }

        /// <summary>
        /// 添加一个新的计划条目。
        /// </summary>
        /// <param name="content">条目内容</param>
        /// <param name="status">条目状态</param>
        /// <param name="priority">条目优先级</param>
        public void AddEntry(string content, PlanEntryStatus? status = null, PlanEntryPriority? priority = null)
        {
            Entries.Add(new PlanEntry
            {
                Content = content,
                Status = status ?? PlanEntryStatus.Pending,
                Priority = priority ?? PlanEntryPriority.Medium
            });
        }

        /// <summary>
        /// 获取所有待处理的条目。
        /// </summary>
        public List<PlanEntry> GetPendingEntries()
        {
            return Entries.FindAll(e => e.Status == PlanEntryStatus.Pending);
        }

        /// <summary>
        /// 获取所有进行中的条目。
        /// </summary>
        public List<PlanEntry> GetInProgressEntries()
        {
            return Entries.FindAll(e => e.Status == PlanEntryStatus.InProgress);
        }

        /// <summary>
        /// 获取所有已完成的条目。
        /// </summary>
        public List<PlanEntry> GetCompletedEntries()
        {
            return Entries.FindAll(e => e.Status == PlanEntryStatus.Completed);
        }

        private static List<PlanEntry> ValidateEntries(List<PlanEntry>? entries)
        {
            if (entries is null)
            {
                throw new JsonException("Plan entries must not be null.");
            }

            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    throw new JsonException("Plan entries must not contain null items.");
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// 计划条目类。
    /// 表示计划中的一个具体任务或步骤。
    /// </summary>
    public class PlanEntry : AcpProtocolObject
    {
        private string _content = string.Empty;

        /// <summary>
        /// 条目的内容描述。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("content")]
        public string Content
        {
            get => _content;
            set => _content = value ?? throw new JsonException("Plan entry content must not be null.");
        }

        /// <summary>
        /// 条目的当前状态。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("status")]
        public PlanEntryStatus Status { get; set; } = PlanEntryStatus.Pending;

        /// <summary>
        /// 条目的优先级。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("priority")]
        public PlanEntryPriority Priority { get; set; } = PlanEntryPriority.Medium;

        /// <summary>
        /// 创建新的计划条目实例。
        /// </summary>
        public PlanEntry()
        {
        }

        /// <summary>
        /// 创建新的计划条目实例。
        /// </summary>
        /// <param name="content">条目内容</param>
        /// <param name="status">条目状态</param>
        /// <param name="priority">条目优先级</param>
        public PlanEntry(string content, PlanEntryStatus? status = null, PlanEntryPriority? priority = null)
        {
            Content = content;
            Status = status ?? PlanEntryStatus.Pending;
            Priority = priority ?? PlanEntryPriority.Medium;
        }

        /// <summary>
        /// 标记条目为进行中。
        /// </summary>
        public void Start()
        {
            Status = PlanEntryStatus.InProgress;
        }

        /// <summary>
        /// 标记条目为已完成。
        /// </summary>
        public void Complete()
        {
            Status = PlanEntryStatus.Completed;
        }
    }

    /// <summary>
    /// 计划条目状态。
    /// </summary>
    /// <remarks>
    /// 建模为可扩展值类型而非封闭枚举，以便无损保留并 round-trip 未知的 wire 值，对齐权威
    /// ACP schema（<c>PlanEntryStatus</c> 为 <c>#[non_exhaustive]</c> 且带 untagged
    /// <c>Other(String)</c> 兜底）。依据 ACP 扩展契约，不以 <c>_</c> 开头的未知值保留给未来
    /// ACP variant，client 不得拒绝。
    /// </remarks>
    [JsonConverter(typeof(PlanEntryStatusJsonConverter))]
    public readonly struct PlanEntryStatus : IEquatable<PlanEntryStatus>
    {
        private readonly string? _value;

        /// <summary>
        /// 以给定 wire 值创建计划条目状态。
        /// </summary>
        /// <param name="value">协议字符串值。</param>
        public PlanEntryStatus(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 条目已创建但尚未开始。
        /// </summary>
        public static PlanEntryStatus Pending { get; } = new("pending");

        /// <summary>
        /// 条目正在执行中。
        /// </summary>
        public static PlanEntryStatus InProgress { get; } = new("in_progress");

        /// <summary>
        /// 条目已成功完成。
        /// </summary>
        public static PlanEntryStatus Completed { get; } = new("completed");

        /// <summary>
        /// 条目在完成前被取消。
        /// </summary>
        public static PlanEntryStatus Cancelled { get; } = new("cancelled");

        /// <summary>
        /// 此状态承载的 wire 值。
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(PlanEntryStatus other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PlanEntryStatus other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// 判断两个状态是否承载相同 wire 值。
        /// </summary>
        public static bool operator ==(PlanEntryStatus left, PlanEntryStatus right) => left.Equals(right);

        /// <summary>
        /// 判断两个状态是否承载不同 wire 值。
        /// </summary>
        public static bool operator !=(PlanEntryStatus left, PlanEntryStatus right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    /// <summary>
    /// 计划条目优先级。
    /// </summary>
    /// <remarks>
    /// 建模为可扩展值类型而非封闭枚举，以便无损保留并 round-trip 未知的 wire 值，对齐权威
    /// ACP schema（<c>PlanEntryPriority</c> 为 <c>#[non_exhaustive]</c> 且带 untagged
    /// <c>Other(String)</c> 兜底）。依据 ACP 扩展契约，不以 <c>_</c> 开头的未知值保留给未来
    /// ACP variant，client 不得拒绝。
    /// </remarks>
    [JsonConverter(typeof(PlanEntryPriorityJsonConverter))]
    public readonly struct PlanEntryPriority : IEquatable<PlanEntryPriority>
    {
        private readonly string? _value;

        /// <summary>
        /// 以给定 wire 值创建计划条目优先级。
        /// </summary>
        /// <param name="value">协议字符串值。</param>
        public PlanEntryPriority(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 低优先级。
        /// </summary>
        public static PlanEntryPriority Low { get; } = new("low");

        /// <summary>
        /// 中等优先级。
        /// </summary>
        public static PlanEntryPriority Medium { get; } = new("medium");

        /// <summary>
        /// 高优先级。
        /// </summary>
        public static PlanEntryPriority High { get; } = new("high");

        /// <summary>
        /// 此优先级承载的 wire 值。
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(PlanEntryPriority other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PlanEntryPriority other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// 判断两个优先级是否承载相同 wire 值。
        /// </summary>
        public static bool operator ==(PlanEntryPriority left, PlanEntryPriority right) => left.Equals(right);

        /// <summary>
        /// 判断两个优先级是否承载不同 wire 值。
        /// </summary>
        public static bool operator !=(PlanEntryPriority left, PlanEntryPriority right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    public sealed class PlanEntryStatusJsonConverter : JsonConverter<PlanEntryStatus>
    {
        public override PlanEntryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Plan entry status must be a string.");
            }

            return new PlanEntryStatus(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryStatus value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    public sealed class PlanEntryPriorityJsonConverter : JsonConverter<PlanEntryPriority>
    {
        public override PlanEntryPriority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Plan entry priority must be a string.");
            }

            return new PlanEntryPriority(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryPriority value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}

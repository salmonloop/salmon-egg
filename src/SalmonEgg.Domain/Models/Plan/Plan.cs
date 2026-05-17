using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Plan
{
    /// <summary>
    /// 计划类。
    /// 表示 Agent 的行动计划，包含一系列计划条目。
    /// </summary>
    public class Plan
    {
        /// <summary>
        /// 计划条目列表。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("entries")]
        public List<PlanEntry> Entries { get; set; } = new List<PlanEntry>();

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
            Entries = entries ?? throw new JsonException("Plan entries must not be null.");
        }

        /// <summary>
        /// 添加一个新的计划条目。
        /// </summary>
        /// <param name="content">条目内容</param>
        /// <param name="status">条目状态</param>
        /// <param name="priority">条目优先级</param>
        public void AddEntry(string content, PlanEntryStatus status = PlanEntryStatus.Pending, PlanEntryPriority priority = PlanEntryPriority.Medium)
        {
            Entries.Add(new PlanEntry
            {
                Content = content,
                Status = status,
                Priority = priority
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
    }

    /// <summary>
    /// 计划条目类。
    /// 表示计划中的一个具体任务或步骤。
    /// </summary>
    public class PlanEntry
    {
        /// <summary>
        /// 条目的内容描述。
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

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
        public PlanEntry(string content, PlanEntryStatus status = PlanEntryStatus.Pending, PlanEntryPriority priority = PlanEntryPriority.Medium)
        {
            Content = content;
            Status = status;
            Priority = priority;
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
    /// 计划条目状态枚举。
    /// 表示计划条目的当前状态。
    /// </summary>
    [JsonConverter(typeof(PlanEntryStatusJsonConverter))]
    public enum PlanEntryStatus
    {
        /// <summary>
        /// 条目已创建但尚未开始。
        /// </summary>
        Pending,

        /// <summary>
        /// 条目正在执行中。
        /// </summary>
        InProgress,

        /// <summary>
        /// 条目已成功完成。
        /// </summary>
        Completed
    }

    /// <summary>
    /// 计划条目优先级枚举。
    /// 表示计划条目的重要程度。
    /// </summary>
    [JsonConverter(typeof(PlanEntryPriorityJsonConverter))]
    public enum PlanEntryPriority
    {
        /// <summary>
        /// 低优先级。
        /// </summary>
        Low,

        /// <summary>
        /// 中等优先级。
        /// </summary>
        Medium,

        /// <summary>
        /// 高优先级。
        /// </summary>
        High
    }

    public sealed class PlanEntryStatusJsonConverter : JsonConverter<PlanEntryStatus>
    {
        public override PlanEntryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Plan entry status must be a string.");
            }

            return reader.GetString() switch
            {
                "pending" => PlanEntryStatus.Pending,
                "in_progress" => PlanEntryStatus.InProgress,
                "completed" => PlanEntryStatus.Completed,
                var value => throw new JsonException($"Unsupported plan entry status '{value}'.")
            };
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryStatus value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                PlanEntryStatus.Pending => "pending",
                PlanEntryStatus.InProgress => "in_progress",
                PlanEntryStatus.Completed => "completed",
                _ => throw new JsonException($"Unsupported plan entry status '{value}'.")
            });
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

            return reader.GetString() switch
            {
                "low" => PlanEntryPriority.Low,
                "medium" => PlanEntryPriority.Medium,
                "high" => PlanEntryPriority.High,
                var value => throw new JsonException($"Unsupported plan entry priority '{value}'.")
            };
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryPriority value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                PlanEntryPriority.Low => "low",
                PlanEntryPriority.Medium => "medium",
                PlanEntryPriority.High => "high",
                _ => throw new JsonException($"Unsupported plan entry priority '{value}'.")
            });
        }
    }
}

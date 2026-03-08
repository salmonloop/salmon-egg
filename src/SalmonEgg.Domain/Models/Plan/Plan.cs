using System;
using System.Collections.Generic;
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
            Entries = entries;
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
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 条目的当前状态。
        /// </summary>
        [JsonPropertyName("status")]
        public PlanEntryStatus Status { get; set; } = PlanEntryStatus.Pending;

        /// <summary>
        /// 条目的优先级。
        /// </summary>
        [JsonPropertyName("priority")]
        public PlanEntryPriority Priority { get; set; } = PlanEntryPriority.Medium;

        /// <summary>
        /// 条目的唯一标识符（可选）。
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// 条目的完成时间（可选）。
        /// </summary>
        [JsonPropertyName("completedAt")]
        public DateTime? CompletedAt { get; set; }

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
            CompletedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 标记条目为失败。
        /// </summary>
        public void Fail()
        {
            Status = PlanEntryStatus.Failed;
        }
    }

    /// <summary>
    /// 计划条目状态枚举。
    /// 表示计划条目的当前状态。
    /// </summary>
    public enum PlanEntryStatus
    {
        /// <summary>
        /// 条目已创建但尚未开始。
        /// </summary>
        [JsonPropertyName("pending")]
        Pending,

        /// <summary>
        /// 条目正在执行中。
        /// </summary>
        [JsonPropertyName("in_progress")]
        InProgress,

        /// <summary>
        /// 条目已成功完成。
        /// </summary>
        [JsonPropertyName("completed")]
        Completed,

        /// <summary>
        /// 条目执行失败。
        /// </summary>
        [JsonPropertyName("failed")]
        Failed
    }

    /// <summary>
    /// 计划条目优先级枚举。
    /// 表示计划条目的重要程度。
    /// </summary>
    public enum PlanEntryPriority
    {
        /// <summary>
        /// 低优先级。
        /// </summary>
        [JsonPropertyName("low")]
        Low,

        /// <summary>
        /// 中等优先级。
        /// </summary>
        [JsonPropertyName("medium")]
        Medium,

        /// <summary>
        /// 高优先级。
        /// </summary>
        [JsonPropertyName("high")]
        High
    }
}

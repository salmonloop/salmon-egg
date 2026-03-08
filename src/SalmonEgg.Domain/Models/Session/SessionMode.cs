using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// 会话模式类。
    /// 表示会话的当前工作模式（如聊天、代码审查、文档编写等）。
    /// </summary>
    public class SessionMode
    {
        /// <summary>
        /// 模式的唯一标识符。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 模式的显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模式的描述信息。
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 创建新的会话模式实例。
        /// </summary>
        public SessionMode()
        {
        }

        /// <summary>
        /// 创建新的会话模式实例。
        /// </summary>
        /// <param name="id">模式 ID</param>
        /// <param name="name">模式名称</param>
        /// <param name="description">模式描述</param>
        public SessionMode(string id, string name, string? description = null)
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// 会话模式状态类。
    /// 包含当前模式和可用模式列表。
    /// </summary>
    public class SessionModeState
    {
        /// <summary>
        /// 当前激活的模式 ID。
        /// </summary>
        [JsonPropertyName("currentModeId")]
        public string CurrentModeId { get; set; } = string.Empty;

        /// <summary>
        /// 当前模式对象。
        /// </summary>
        [JsonIgnore]
        public SessionMode? CurrentMode { get; set; }

        /// <summary>
        /// 可用的模式列表。
        /// </summary>
        [JsonPropertyName("availableModes")]
        public List<SessionMode> AvailableModes { get; set; } = new List<SessionMode>();

        /// <summary>
        /// 创建新的会话模式状态实例。
        /// </summary>
        public SessionModeState()
        {
        }

        /// <summary>
        /// 根据 ID 获取当前模式。
        /// </summary>
        public SessionMode? GetModeById(string modeId)
        {
            return AvailableModes.Find(m => m.Id == modeId);
        }

        /// <summary>
        /// 判断指定模式是否可用。
        /// </summary>
        public bool IsModeAvailable(string modeId)
        {
            return AvailableModes.Exists(m => m.Id == modeId);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
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
        /// 深拷贝本状态：可用模式列表与其中每个 <see cref="SessionMode"/> 都会被复制，
        /// <see cref="CurrentMode"/> 由副本自己的列表重新解析（而非照抄源对象的引用），
        /// 使副本内部一致、与源对象完全不共享可变状态。
        /// </summary>
        /// <remarks>
        /// 这里不实现 <c>ICloneable</c>：该接口无法表达"深拷贝还是浅拷贝"，官方设计准则
        /// 因此不建议实现它，改为提供语义明确的具名方法。
        /// 本类型可变，凡是跨越所有权边界（写入 <see cref="Session"/> 或从其读出）都必须复制，
        /// 否则调用方与聚合会共享同一个可变对象，绕过聚合的同步。
        /// </remarks>
        public SessionModeState DeepCopy()
        {
            var copy = new SessionModeState
            {
                CurrentModeId = CurrentModeId,
                AvailableModes = AvailableModes
                    .Select(static mode => new SessionMode(mode.Id, mode.Name, mode.Description))
                    .ToList()
            };
            copy.CurrentMode = copy.GetModeById(copy.CurrentModeId);
            return copy;
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

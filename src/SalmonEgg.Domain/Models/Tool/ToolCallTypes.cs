using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Tool
{
    /// <summary>
    /// 工具调用的状态枚举。
    /// 表示工具调用在生命周期中的当前状态。
    /// </summary>
    public enum ToolCallStatus
    {
        /// <summary>
        /// 工具调用已创建但尚未开始执行。
        /// </summary>
        [JsonPropertyName("pending")]
        Pending,

        /// <summary>
        /// 工具调用正在执行中。
        /// </summary>
        [JsonPropertyName("in_progress")]
        InProgress,

        /// <summary>
        /// 工具调用已成功完成。
        /// </summary>
        [JsonPropertyName("completed")]
        Completed,

        /// <summary>
        /// 工具调用失败或出错。
        /// </summary>
        [JsonPropertyName("failed")]
        Failed
    }

    /// <summary>
    /// 工具调用的类型枚举。
    /// 表示工具执行的具体操作类型。
    /// </summary>
    public enum ToolCallKind
    {
        /// <summary>
        /// 文件读取操作。
        /// </summary>
        [JsonPropertyName("read")]
        Read,

        /// <summary>
        /// 文件编辑操作。
        /// </summary>
        [JsonPropertyName("edit")]
        Edit,

        /// <summary>
        /// 文件删除操作。
        /// </summary>
        [JsonPropertyName("delete")]
        Delete,

        /// <summary>
        /// 文件移动或重命名操作。
        /// </summary>
        [JsonPropertyName("move")]
        Move,

        /// <summary>
        /// 搜索操作。
        /// </summary>
        [JsonPropertyName("search")]
        Search,

        /// <summary>
        /// 终端命令执行操作。
        /// </summary>
        [JsonPropertyName("execute")]
        Execute,

        /// <summary>
        /// 思考或推理操作（不执行实际动作）。
        /// </summary>
        [JsonPropertyName("think")]
        Think,

        /// <summary>
        /// 网络请求或数据获取操作。
        /// </summary>
        [JsonPropertyName("fetch")]
        Fetch,

        /// <summary>
        /// 其他未分类的工具调用。
        /// </summary>
        [JsonPropertyName("other")]
        Other
    }
}

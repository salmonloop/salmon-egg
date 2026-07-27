namespace SalmonEgg.Domain.Models;

/// <summary>
/// 传输类型枚举
/// </summary>
public enum TransportType
{
    /// <summary>
    /// Stdio (标准输入/输出) 传输 - 用于子进程 Agent 或桥接进程
    /// </summary>
    Stdio,

    /// <summary>
    /// WebSocket 传输 - 用于远程 Agent
    /// </summary>
    WebSocket,

    /// <summary>
    /// Streamable HTTP 传输(ACP 官方草案:单端点 POST + 连接/会话级 SSE 流)- 用于远程 Agent。
    /// 持久化 canonical token 为 streamable_http。
    /// </summary>
    StreamableHttp
}

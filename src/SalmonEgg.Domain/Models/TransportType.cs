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
    /// HTTP Server-Sent Events (SSE) 传输 - 用于远程 Agent
    /// </summary>
    HttpSse
}

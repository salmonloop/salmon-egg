using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnoAcpClient.Domain.Models.Content;
using UnoAcpClient.Domain.Models.Plan;
using UnoAcpClient.Domain.Models.Protocol;
using UnoAcpClient.Domain.Models.Session;
using UnoAcpClient.Domain.Models.Tool;
using UnoAcpClient.Domain.Services.Security;

namespace UnoAcpClient.Domain.Services
{
    /// <summary>
    /// ACP 客户端接口。
    /// 定义了与 Agent 通信的核心方法。
    /// </summary>
    public interface IAcpClient
    {
        /// <summary>
        /// 初始化事件。当初始化完成时触发。
        /// </summary>
        event EventHandler<InitializeResponse>? Initialized;

        /// <summary>
        /// 会话更新事件。当收到会话更新通知时触发。
        /// </summary>
        event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        /// <summary>
        /// 权限请求事件。当收到权限请求时触发。
        /// </summary>
        event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;

        /// <summary>
        /// 文件系统请求事件。当收到文件系统操作请求时触发。
        /// </summary>
        event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;

        /// <summary>
        /// 连接错误事件。当发生连接错误时触发。
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 判断客户端是否已初始化。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 判断是否已连接到 Agent。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取当前的 Agent 信息。
        /// </summary>
        AgentInfo? AgentInfo { get; }

        /// <summary>
        /// 获取当前的 Agent 能力。
        /// </summary>
        AgentCapabilities? AgentCapabilities { get; }

        /// <summary>
        /// 初始化与 Agent 的连接。
        /// 发送 initialize 请求并等待 Agent 响应。
        /// </summary>
        /// <param name="params">初始化参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>初始化响应</returns>
        Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 创建新的会话。
        /// 发送 session/new 请求并等待 Agent 响应。
        /// </summary>
        /// <param name="params">创建会话参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>创建会话响应</returns>
        Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 加载已有的会话。
        /// 发送 session/load 请求并等待 Agent 通过 session/update 通知重放历史。
        /// </summary>
        /// <param name="params">加载会话参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载会话响应</returns>
        Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 向会话发送提示。
        /// 发送 session/prompt 请求并等待 Agent 响应。
        /// </summary>
        /// <param name="params">发送提示参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送提示响应</returns>
        Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置会话模式。
        /// 发送 session/set_mode 请求。
        /// </summary>
        /// <param name="params">设置模式参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>设置模式响应</returns>
        Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置会话配置选项。
        /// 发送 session/set_config_option 请求。
        /// </summary>
        /// <param name="params">设置配置参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>设置配置响应</returns>
        Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 取消会话。
        /// 发送 session/cancel 请求。
        /// </summary>
        /// <param name="params">取消会话参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>取消会话响应</returns>
        Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行认证。
        /// 发送 authenticate 请求。
        /// </summary>
        /// <param name="params">认证参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>认证响应</returns>
        Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 响应权限请求。
        /// 发送对之前权限请求的响应。
        /// </summary>
        /// <param name="messageId">原始请求的消息 ID</param>
        /// <param name="outcome">结果（selected, cancelled, denied 等）</param>
        /// <param name="optionId">选中的选项 ID（可选）</param>
        /// <returns>是否成功发送响应</returns>
        Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null);

        /// <summary>
        /// 响应文件系统请求。
        /// 发送对之前文件系统请求的响应。
        /// </summary>
        /// <param name="messageId">原始请求的消息 ID</param>
        /// <param name="success">是否成功</param>
        /// <param name="content">文件内容（读取操作）</param>
        /// <param name="message">错误消息（如果失败）</param>
        /// <returns>是否成功发送响应</returns>
        Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null);

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        /// <returns>是否成功断开</returns>
        Task<bool> DisconnectAsync();
    }

    /// <summary>
    /// 会话更新事件参数。
    /// </summary>
    public class SessionUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// 会话 ID。
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 更新内容。
        /// </summary>
        public SessionUpdate? Update { get; set; }

        /// <summary>
        /// 创建新的会话更新事件参数。
        /// </summary>
        public SessionUpdateEventArgs()
        {
        }

        /// <summary>
        /// 创建新的会话更新事件参数。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="update">更新内容</param>
        public SessionUpdateEventArgs(string sessionId, SessionUpdate? update)
        {
            SessionId = sessionId;
            Update = update;
        }
    }

    /// <summary>
    /// 权限请求事件参数。
    /// </summary>
    public class PermissionRequestEventArgs : EventArgs
    {
        /// <summary>
        /// 原始请求的消息 ID。
        /// </summary>
        public object MessageId { get; set; } = string.Empty;

        /// <summary>
        /// 会话 ID。
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 工具调用数据。
        /// </summary>
        public object? ToolCall { get; set; }

        /// <summary>
        /// 可用的权限选项列表。
        /// </summary>
        public List<PermissionOption> Options { get; set; } = new List<PermissionOption>();

        /// <summary>
        /// 响应回调。
        /// </summary>
        public Func<string, string?, Task> Respond { get; set; } = null!;

        /// <summary>
        /// 创建新的权限请求事件参数。
        /// </summary>
        public PermissionRequestEventArgs()
        {
        }

        /// <summary>
        /// 创建新的权限请求事件参数。
        /// </summary>
        /// <param name="messageId">消息 ID</param>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="toolCall">工具调用</param>
        /// <param name="options">权限选项</param>
        /// <param name="respond">响应回调</param>
        public PermissionRequestEventArgs(
            object messageId,
            string sessionId,
            object? toolCall,
            List<PermissionOption> options,
            Func<string, string?, Task> respond)
        {
            MessageId = messageId;
            SessionId = sessionId;
            ToolCall = toolCall;
            Options = options;
            Respond = respond;
        }
    }

    /// <summary>
    /// 文件系统请求事件参数。
    /// </summary>
    public class FileSystemRequestEventArgs : EventArgs
    {
        /// <summary>
        /// 原始请求的消息 ID。
        /// </summary>
        public object MessageId { get; set; } = string.Empty;

        /// <summary>
        /// 会话 ID。
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 操作类型（"read" 或 "write"）。
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// 文件路径。
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 文件编码（读取操作）。
        /// </summary>
        public string? Encoding { get; set; }

        /// <summary>
        /// 文件内容（写入操作）。
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 响应回调。
        /// </summary>
        public Func<bool, string?, string?, Task> Respond { get; set; } = null!;

        /// <summary>
        /// 创建新的文件系统请求事件参数。
        /// </summary>
        public FileSystemRequestEventArgs()
        {
        }

        /// <summary>
        /// 创建新的文件系统请求事件参数。
        /// </summary>
        /// <param name="messageId">消息 ID</param>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="operation">操作类型</param>
        /// <param name="path">文件路径</param>
        /// <param name="encoding">编码</param>
        /// <param name="content">内容</param>
        /// <param name="respond">响应回调</param>
        public FileSystemRequestEventArgs(
            object messageId,
            string sessionId,
            string operation,
            string path,
            string? encoding = null,
            string? content = null,
            Func<bool, string?, string?, Task> respond = null!)
        {
            MessageId = messageId;
            SessionId = sessionId;
            Operation = operation;
            Path = path;
            Encoding = encoding;
            Content = content;
            Respond = respond;
        }
    }
}

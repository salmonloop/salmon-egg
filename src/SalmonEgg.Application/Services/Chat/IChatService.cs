using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.Services.Chat
{
    /// <summary>
    /// Chat 服务接口
    /// 提供与 ACP 协议相关的聊天功能，包括会话管理、消息发送、权限处理等
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// 当前会话 ID
        /// </summary>
        string? CurrentSessionId { get; }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Agent 信息
        /// </summary>
        AgentInfo? AgentInfo { get; }

        /// <summary>
        /// Agent 能力
        /// </summary>
        AgentCapabilities? AgentCapabilities { get; }

        /// <summary>
        /// 当前会话历史
        /// </summary>
        IReadOnlyList<SessionUpdateEntry> SessionHistory { get; }

        /// <summary>
        /// 当前计划
        /// </summary>
        Plan? CurrentPlan { get; }

        /// <summary>
        /// 当前会话模式
        /// </summary>
        SessionModeState? CurrentMode { get; }

        /// <summary>
        /// 会话更新事件
        /// </summary>
        event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        /// <summary>
        /// 权限请求事件
        /// </summary>
        event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;

        /// <summary>
        /// 文件系统请求事件
        /// </summary>
        event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;

        /// <summary>
        /// 终端请求事件
        /// </summary>
        event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;

        /// <summary>
        /// 错误事件
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 初始化与 Agent 的连接
        /// </summary>
        Task<InitializeResponse> InitializeAsync(InitializeParams @params);

        /// <summary>
        /// 创建新的会话
        /// </summary>
        Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params);

        /// <summary>
        /// 加载现有会话
        /// </summary>
        Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params);

        /// <summary>
        /// 向会话发送提示消息
        /// </summary>
        Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置会话模式
        /// </summary>
        Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params);

        /// <summary>
        /// 设置会话配置选项
        /// </summary>
        Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params);

        /// <summary>
        /// 取消会话
        /// </summary>
        Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params);

        /// <summary>
        /// 执行认证（当 Agent 在 initialize 响应中返回 authMethods 时）。
        /// </summary>
        Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// 响应权限请求
        /// </summary>
        Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null);

        /// <summary>
        /// 响应文件系统请求
        /// </summary>
        Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null);

        /// <summary>
        /// 断开连接
        /// </summary>
        Task<bool> DisconnectAsync();

        /// <summary>
        /// 获取支持的会话模式列表
        /// </summary>
        Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync();

        /// <summary>
        /// 清除会话历史
        /// </summary>
        void ClearHistory();
    }
}

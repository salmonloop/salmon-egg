using System;
using System.Threading.Tasks;
using SalmonEgg.Application.Common;

namespace SalmonEgg.Application.Services.Chat
{
    /// <summary>
    /// 错误恢复服务接口
    /// 提供连接错误、会话错误、文件系统错误和协议版本错误的恢复策略
    /// </summary>
    public interface IErrorRecoveryService
    {
        /// <summary>
        /// 连接错误恢复策略
        /// 实现自动重连和指数退避
        /// </summary>
        /// <param name="error">错误信息</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="initialDelayMs">初始延迟（毫秒）</param>
        /// <returns>重连结果</returns>
        Task<Result> RecoverFromConnectionErrorAsync(string error, int maxRetries = 3, int initialDelayMs = 1000);

        /// <summary>
        /// 会话错误恢复策略
        /// 实现自动创建新会话和状态同步
        /// </summary>
        /// <param name="sessionId">原会话 ID</param>
        /// <param name="error">错误信息</param>
        /// <returns>恢复结果（新会话 ID）</returns>
        Task<Result<string>> RecoverFromSessionErrorAsync(string sessionId, string error);

        /// <summary>
        /// 文件系统错误恢复策略
        /// 实现权限请求和路径验证
        /// </summary>
        /// <param name="operation">操作类型（read/write）</param>
        /// <param name="path">文件路径</param>
        /// <param name="error">错误信息</param>
        /// <returns>恢复结果（是否成功）</returns>
        Task<Result<bool>> RecoverFromFileSystemErrorAsync(string operation, string path, string error);

        /// <summary>
        /// 协议版本错误恢复策略
        /// 实现版本不兼容时的处理（显示升级指引）
        /// </summary>
        /// <param name="expectedVersion">期望的协议版本</param>
        /// <param name="actualVersion">实际的协议版本</param>
        /// <returns>恢复结果（是否可以继续）</returns>
        Task<Result> RecoverFromProtocolVersionErrorAsync(int expectedVersion, int actualVersion);

        /// <summary>
        /// 获取当前的重试计数
        /// </summary>
        int GetCurrentRetryCount();

        /// <summary>
        /// 重置重试计数
        /// </summary>
        void ResetRetryCount();

        /// <summary>
        /// 获取错误恢复策略配置
        /// </summary>
        ErrorRecoveryConfig GetConfig();

        /// <summary>
        /// 设置错误恢复策略配置
        /// </summary>
        void SetConfig(ErrorRecoveryConfig config);
    }

    /// <summary>
    /// 错误恢复策略配置
    /// </summary>
    public class ErrorRecoveryConfig
    {
        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// 初始延迟（毫秒）
        /// </summary>
        public int InitialDelayMs { get; set; } = 1000;

        /// <summary>
        /// 最大延迟（毫秒）
        /// </summary>
        public int MaxDelayMs { get; set; } = 30000;

        /// <summary>
        /// 延迟增长因子（指数退避）
        /// </summary>
        public double DelayMultiplier { get; set; } = 2.0;

        /// <summary>
        /// 是否启用会话自动恢复
        /// </summary>
        public bool EnableSessionAutoRecovery { get; set; } = true;

        /// <summary>
        /// 是否启用文件系统错误恢复
        /// </summary>
        public bool EnableFileSystemRecovery { get; set; } = true;

        /// <summary>
        /// 是否显示协议版本错误提示
        /// </summary>
        public bool ShowProtocolVersionWarning { get; set; } = true;
    }
}

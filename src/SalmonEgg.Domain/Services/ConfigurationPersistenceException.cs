using System;

namespace SalmonEgg.Domain.Services;

public sealed class ConfigurationPersistenceException : Exception
{
    public ConfigurationPersistenceException(
        ConfigurationPersistenceFailureReason reason,
        string userMessage,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        Reason = reason;
        UserMessage = userMessage;
    }

    public ConfigurationPersistenceFailureReason Reason { get; }

    public string UserMessage { get; }
}

public enum ConfigurationPersistenceFailureReason
{
    SecureStorageUnavailable,
    SecretPersistenceFailed,
    ConfigurationWriteFailed,
    ConfigurationReadFailed,
    ConfigurationRollbackFailed,
    SecureStorageCleanupFailed,
    ConfigurationDeleteFailed,
    ConfigurationConflict,
    ConfigurationLockUnavailable,
    ConfigurationRecoveryRequired,

    /// <summary>
    /// 磁盘上的配置 schema_version 比本程序支持的更新；写入被拒绝以保护前向数据。
    /// </summary>
    /// <remarks>
    /// 语义是「拒绝写回」而非「写入失败」：文件本身完好，只是不能被旧程序覆盖。
    /// 调用方据此给出升级指引，而不是把用户引向重试或权限排查。
    /// </remarks>
    SchemaVersionTooNew
}

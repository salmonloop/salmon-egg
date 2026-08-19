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
    ConfigurationRecoveryRequired
}

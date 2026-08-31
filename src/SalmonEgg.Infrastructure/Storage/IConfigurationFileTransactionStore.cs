using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Owns both reads and rollback-capable mutations for server configuration files.
/// </summary>
/// <remarks>
/// Keeping these capabilities on one instance prevents configuration reads, recovery, and writes
/// from observing different load state or persistence backends.
/// </remarks>
public interface IConfigurationFileStore : IAppFileStore, IConfigurationFileTransactionStore
{
}

/// <summary>
/// Provides rollback-capable mutations for one server configuration YAML file.
/// </summary>
/// <remarks>
/// This companion capability intentionally does not extend <c>IAppFileStore</c>:
/// generic application files do not participate in a paired YAML/secure-storage operation.
/// </remarks>
public interface IConfigurationFileTransactionStore
{
    Task<IConfigurationFileTransaction> BeginWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default);

    Task<IConfigurationFileTransaction> BeginDeleteAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Names the recovery artifacts a configuration-file transaction stages next to its target file.
/// </summary>
/// <remarks>
/// Callers that enumerate the configuration root (package export, backup, restore) must skip these:
/// they are in-flight recovery material for one device, not portable configuration content.
/// </remarks>
public static class ConfigurationFileTransactionArtifacts
{
    public const string PendingSuffix = ".pending.";

    public const string RollbackSuffix = ".rollback.";

    public static bool IsArtifact(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var fileName = System.IO.Path.GetFileName(path);
        return fileName.Contains(PendingSuffix, StringComparison.Ordinal)
            || fileName.Contains(RollbackSuffix, StringComparison.Ordinal);
    }
}

/// <summary>
/// Represents a staged configuration-file mutation whose original state remains recoverable until completion.
/// </summary>
public interface IConfigurationFileTransaction : IAsyncDisposable
{
    /// <summary>
    /// Applies the candidate state and flushes it while retaining recovery material.
    /// </summary>
    Task ApplyAndFlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the pre-transaction file state and flushes that restoration.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the mutation after all paired persistence work has completed.
    /// </summary>
    void Complete();
}

/// <summary>
/// Indicates that applying a configuration-file mutation failed and restoring the original file state also failed.
/// </summary>
public sealed class ConfigurationFileRollbackException : Exception
{
    public ConfigurationFileRollbackException(Exception operationException, Exception rollbackException)
        : base("The configuration-file mutation failed and its rollback could not be completed.",
            new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException ?? throw new ArgumentNullException(nameof(operationException));
        RollbackException = rollbackException ?? throw new ArgumentNullException(nameof(rollbackException));
    }

    public Exception OperationException { get; }

    public Exception RollbackException { get; }
}

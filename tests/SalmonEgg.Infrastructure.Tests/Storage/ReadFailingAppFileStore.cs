using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

/// <summary>
/// 仅在读现有配置文件时抛 <see cref="IOException"/> 的 <see cref="IAppFileStore"/>，
/// 用于验证 <see cref="ConfigurationManager"/> 预检 schema 阶段的 I/O 失败被包装为
/// <see cref="ConfigurationPersistenceException"/>。
/// </summary>
internal sealed class ReadFailingAppFileStore : IConfigurationFileStore
{
    private readonly FileSystemAppFileStore _inner = new();

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ExistsAsync(path, cancellationToken);

    public Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => path.Contains($"{Path.DirectorySeparatorChar}recovery{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? _inner.ReadAllTextAsync(path, cancellationToken)
            : throw new IOException("read failed");

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => _inner.WriteAllTextAsync(path, content, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(path, cancellationToken);

    public IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        CancellationToken cancellationToken = default)
        => _inner.EnumerateFilesAsync(directory, searchPattern, cancellationToken);

    public Task<IConfigurationFileTransaction> BeginWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
        => _inner.BeginWriteAsync(path, content, cancellationToken);

    public Task<IConfigurationFileTransaction> BeginDeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
        => _inner.BeginDeleteAsync(path, cancellationToken);
}

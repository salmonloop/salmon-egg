using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IAppFileStore
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        CancellationToken cancellationToken = default);
}

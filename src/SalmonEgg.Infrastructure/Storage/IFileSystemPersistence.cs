using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Synchronizes platform-backed file system state for infrastructure file access.
/// </summary>
public interface IFileSystemPersistence
{
    /// <summary>
    /// Loads any platform-backed file system state before application files are read or written.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists any pending file system changes to the platform backing store.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

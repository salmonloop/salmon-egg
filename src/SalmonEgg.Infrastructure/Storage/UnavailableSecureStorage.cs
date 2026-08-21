using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// A secret store that is always unavailable.
/// </summary>
/// <remarks>
/// Used as the primary store on platforms that offer no OS secret store, so a host running under
/// <see cref="SecureStorageDowngradePolicy.FailClosed"/> reaches the same "platform store unavailable"
/// path there as it would on a desktop whose keyring is missing. Without this the absence of any
/// platform store would be indistinguishable from a working one and writes would land in plaintext.
/// </remarks>
public sealed class UnavailableSecureStorage : ISecureStorage
{
    public static UnavailableSecureStorage Instance { get; } = new();

    private const string Message = "No platform secret store is available on this operating system.";

    public Task SaveAsync(string key, string value)
        => throw new SecureStorageUnavailableException(Message);

    public Task<string?> LoadAsync(string key)
        => throw new SecureStorageUnavailableException(Message);

    public Task DeleteAsync(string key)
        => throw new SecureStorageUnavailableException(Message);
}

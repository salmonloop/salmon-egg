using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Owns startup recovery for interrupted configuration transactions.
/// </summary>
public interface IConfigurationRecoveryService
{
    Task RecoverPendingTransactionsAsync(CancellationToken cancellationToken = default);
}

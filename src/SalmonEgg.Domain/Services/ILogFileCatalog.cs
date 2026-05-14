using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Domain.Services;

public interface ILogFileCatalog
{
    Task<LogFileSummary?> GetLatestAsync(string logsDirectoryPath, CancellationToken cancellationToken = default);

    Task<string?> ReadTailAsync(string filePath, int maxChars, CancellationToken cancellationToken = default);
}

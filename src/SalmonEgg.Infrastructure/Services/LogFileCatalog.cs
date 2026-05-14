using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class LogFileCatalog : ILogFileCatalog
{
    public Task<LogFileSummary?> GetLatestAsync(string logsDirectoryPath, CancellationToken cancellationToken = default)
    {
        if (logsDirectoryPath is null)
        {
            throw new ArgumentNullException(nameof(logsDirectoryPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(logsDirectoryPath))
        {
            return Task.FromResult<LogFileSummary?>(null);
        }

        try
        {
            var latest = Directory.EnumerateFiles(logsDirectoryPath, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            return Task.FromResult(latest is null
                ? null
                : new LogFileSummary(latest.FullName, new DateTimeOffset(latest.LastWriteTimeUtc, TimeSpan.Zero)));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult<LogFileSummary?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<LogFileSummary?>(null);
        }
        catch (IOException)
        {
            return Task.FromResult<LogFileSummary?>(null);
        }
    }

    public async Task<string?> ReadTailAsync(string filePath, int maxChars, CancellationToken cancellationToken = default)
    {
        if (filePath is null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return text.Length <= maxChars ? text : text.Substring(text.Length - maxChars, maxChars);
    }
}

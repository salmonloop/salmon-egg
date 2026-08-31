using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class LiveLogStreamService : ILiveLogStreamService
{
    private const int InitialTailCharacterLimit = 8192;
    private static readonly Encoding Utf8 = Encoding.UTF8;
    private readonly TimeSpan _pollInterval;

    public LiveLogStreamService(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task StartAsync(
        string logsDirectoryPath,
        Func<LiveLogStreamUpdate, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        if (logsDirectoryPath is null)
        {
            throw new ArgumentNullException(nameof(logsDirectoryPath));
        }

        if (onUpdate is null)
        {
            throw new ArgumentNullException(nameof(onUpdate));
        }

        string? currentFile = null;
        long currentOffset = 0;
        var hasSelectedInitialFile = false;
        var hasReportedMissingFile = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var latestFile = GetLatestLogFile(logsDirectoryPath);
            if (latestFile is null)
            {
                if (currentFile is not null || !hasReportedMissingFile)
                {
                    currentFile = null;
                    currentOffset = 0;
                    hasReportedMissingFile = true;

                    await onUpdate(new LiveLogStreamUpdate(null, string.Empty, hasFileSwitched: true))
                        .ConfigureAwait(false);
                }
            }
            else if (!string.Equals(currentFile, latestFile, StringComparison.Ordinal))
            {
                var isInitialFileSelection = !hasSelectedInitialFile;
                currentFile = latestFile;
                currentOffset = GetFileLength(currentFile);
                hasReportedMissingFile = false;

                var initialTailText = isInitialFileSelection
                    ? await ReadRecentTailAsync(currentFile, InitialTailCharacterLimit, cancellationToken).ConfigureAwait(false)
                    : string.Empty;

                await onUpdate(new LiveLogStreamUpdate(currentFile, initialTailText, hasFileSwitched: true))
                    .ConfigureAwait(false);
                hasSelectedInitialFile = true;
            }

            if (currentFile is not null)
            {
                var (text, newPosition) = await ReadAppendedTextAsync(currentFile, currentOffset, cancellationToken)
                    .ConfigureAwait(false);
                currentOffset = newPosition;

                if (!string.IsNullOrEmpty(text))
                {
                    await onUpdate(new LiveLogStreamUpdate(currentFile, text, hasFileSwitched: false))
                        .ConfigureAwait(false);
                }
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? GetLatestLogFile(string logsDirectoryPath)
    {
        if (!Directory.Exists(logsDirectoryPath))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(logsDirectoryPath, "*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static long GetFileLength(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static async Task<(string Text, long NewPosition)> ReadAppendedTextAsync(
        string filePath,
        long startOffset,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var safeOffset = Math.Min(startOffset, stream.Length);
            stream.Seek(safeOffset, SeekOrigin.Begin);

            var builder = new StringBuilder();
            var buffer = new byte[4096];
            // Decode across chunk boundaries with a stateful Decoder so multi-byte
            // code points split across reads are reassembled instead of being
            // corrupted into replacement characters. Any trailing partial sequence
            // at end-of-stream is flushed as-is (UTF-8 REPLACEMENT char), matching
            // the prior behavior for a truncated final byte.
            var decoder = Utf8.GetDecoder();
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false)) > 0)
            {
                var chars = new char[Utf8.GetCharCount(buffer, 0, bytesRead)];
                var charCount = decoder.GetChars(buffer, 0, bytesRead, chars, 0);
                builder.Append(chars, 0, charCount);
            }

            return (builder.ToString(), stream.Position);
        }
        catch (FileNotFoundException)
        {
            return (string.Empty, startOffset);
        }
        catch (IOException)
        {
            return (string.Empty, startOffset);
        }
    }

    private static async Task<string> ReadRecentTailAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var maxBytesToRead = Math.Min(stream.Length, maxCharacters * 4L);
            var startOffset = Math.Max(0, stream.Length - maxBytesToRead);
            // Align the tail start to a UTF-8 leading byte so a multi-byte code
            // point is not split at the read boundary. A UTF-8 continuation byte
            // has the 10xxxxxx bit pattern; walk forward past any such bytes
            // that belong to a code point started before the offset.
            startOffset = AlignToUtf8LeadingByte(stream, startOffset);
            stream.Seek(startOffset, SeekOrigin.Begin);

            var buffer = new byte[maxBytesToRead];
            var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, (int)maxBytesToRead),
                    cancellationToken)
                .ConfigureAwait(false);
            var text = Utf8.GetString(buffer, 0, bytesRead);
            if (text.Length <= maxCharacters)
            {
                return text;
            }

            return text.Substring(text.Length - maxCharacters, maxCharacters);
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static long AlignToUtf8LeadingByte(FileStream stream, long offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        // Skip up to 3 continuation bytes (10xxxxxx) so the read begins on a
        // leading byte of a UTF-8 code point. 3 is the max trailing-byte count
        // for a 4-byte UTF-8 sequence. ReadByte() returns -1 at end-of-stream.
        var probe = offset;
        var maxSkip = Math.Min(3L, stream.Length - offset);
        while (maxSkip > 0)
        {
            stream.Seek(probe, SeekOrigin.Begin);
            var first = stream.ReadByte();
            if (first < 0 || !IsUtf8ContinuationByte(first))
            {
                break;
            }

            probe++;
            maxSkip--;
        }

        return probe;
    }

    private static bool IsUtf8ContinuationByte(int value)
        => (value & 0xC0) == 0x80;
}

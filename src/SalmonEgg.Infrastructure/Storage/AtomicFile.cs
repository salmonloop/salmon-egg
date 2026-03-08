using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

internal static class AtomicFile
{
    internal static async Task WriteUtf8AtomicAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    return;
                }
                catch
                {
                    // Fall back to non-atomic replace if File.Replace isn't supported by the underlying FS.
                }

                File.Delete(path);
            }

            File.Move(tempPath, path);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }
    }
}

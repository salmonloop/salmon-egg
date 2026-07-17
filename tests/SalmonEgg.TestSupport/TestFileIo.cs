using System.Text;

namespace SalmonEgg.TestSupport;

/// <summary>
/// Cross-platform file write helper for deterministic fixtures that may race with exclusive locks.
/// Shared by unit tests and platform GUI harnesses.
/// </summary>
public static class TestFileIo
{
    public static void WriteAllTextWithRetry(
        string path,
        string contents,
        Encoding encoding,
        int attempts = 5,
        int delayMilliseconds = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(encoding);

        IOException? lastIOException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.WriteAllText(path, contents, encoding);
                return;
            }
            catch (IOException ex) when (attempt < attempts)
            {
                lastIOException = ex;
                Thread.Sleep(delayMilliseconds);
            }
        }

        throw lastIOException ?? new IOException($"Failed to write '{path}'.");
    }
}

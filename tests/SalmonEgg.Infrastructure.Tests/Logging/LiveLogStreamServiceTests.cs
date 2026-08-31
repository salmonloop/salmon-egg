using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Logging;

public sealed class LiveLogStreamServiceTests
{
    [Fact]
    public void LiveLogStreamUpdate_ExposesExpectedShape()
    {
        var update = new LiveLogStreamUpdate(
            currentLogFilePath: "log.txt",
            appendedText: "hello",
            hasFileSwitched: false);

        Assert.Equal("log.txt", update.CurrentLogFilePath);
        Assert.Equal("hello", update.AppendedText);
        Assert.False(update.HasFileSwitched);
    }

    [Fact]
    public async Task StartAsync_ReplaysInitialTailThenAppendsOnlyNewText()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var logFile = Path.Combine(tempRoot, "app.log");
            await File.WriteAllTextAsync(logFile, "line-1\n", TestContext.Current.CancellationToken);

            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(30));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await WaitForConditionAsync(
                () => updates.Any(u => u.AppendedText.Contains("line-1", StringComparison.Ordinal)),
                cts.Token);
            await File.AppendAllTextAsync(logFile, "line-2\n", TestContext.Current.CancellationToken);
            await WaitForConditionAsync(
                () => updates.Any(u => u.AppendedText.Contains("line-2", StringComparison.Ordinal)),
                cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.Contains(
                updates,
                update => update.AppendedText.Contains("line-1", StringComparison.Ordinal));
            Assert.Contains(updates, update => string.Equals(update.AppendedText, "line-2\n", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task StartAsync_EmitsRecentTailFromInitialLogFile()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var logFile = Path.Combine(tempRoot, "app.log");
            await File.WriteAllTextAsync(logFile, "line-1\nline-2\nline-3\n", TestContext.Current.CancellationToken);

            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(30));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await WaitForConditionAsync(
                () => updates.Any(u => u.AppendedText.Contains("line-3", StringComparison.Ordinal)),
                cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.Contains(updates, update => update.AppendedText.Contains("line-2", StringComparison.Ordinal));
            Assert.Contains(updates, update => update.AppendedText.Contains("line-3", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task StartAsync_SwitchesFilesWithoutReplayingExistingContent()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var fileA = Path.Combine(tempRoot, "a.log");
            var fileB = Path.Combine(tempRoot, "b.log");
            await File.WriteAllTextAsync(fileA, "initial-a\n", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(fileA, DateTime.UtcNow.AddSeconds(-2));

            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(25));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await File.AppendAllTextAsync(fileA, "after-start\n", TestContext.Current.CancellationToken);
            await WaitForConditionAsync(() => updates.Any(u => u.AppendedText.Contains("after-start")), cts.Token);

            await File.WriteAllTextAsync(fileB, "initial-b\n", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(fileB, DateTime.UtcNow.AddSeconds(2));
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            await File.AppendAllTextAsync(fileB, "second-file-late\n", TestContext.Current.CancellationToken);

            await WaitForConditionAsync(
                () => updates.Any(
                    u => u.HasFileSwitched
                        && string.Equals(u.CurrentLogFilePath, fileB, StringComparison.Ordinal)),
                cts.Token);
            await WaitForConditionAsync(
                () => updates.Any(
                    u => string.Equals(u.CurrentLogFilePath, fileB, StringComparison.Ordinal)
                        && u.AppendedText.Contains("second-file-late", StringComparison.Ordinal)),
                cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.Contains(
                updates,
                u => u.HasFileSwitched
                    && string.Equals(u.CurrentLogFilePath, fileB, StringComparison.Ordinal));
            var appendedTexts = updates.Where(u => !string.IsNullOrEmpty(u.AppendedText)).Select(u => u.AppendedText);
            Assert.DoesNotContain(appendedTexts, text => text.Contains("initial-b"));
            Assert.Contains(appendedTexts, text => text.Contains("second-file-late"));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task StartAsync_HandlesMissingDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(30));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await WaitForConditionAsync(
                () => updates.Any(
                    u => u.HasFileSwitched
                        && string.IsNullOrWhiteSpace(u.CurrentLogFilePath)
                        && string.IsNullOrEmpty(u.AppendedText)),
                cts.Token);
            Directory.CreateDirectory(tempRoot);
            var logFile = Path.Combine(tempRoot, "fresh.log");
            await File.WriteAllTextAsync(logFile, string.Empty, TestContext.Current.CancellationToken);

            await WaitForConditionAsync(() => updates.Any(u => u.HasFileSwitched), cts.Token);
            await File.AppendAllTextAsync(logFile, "hello\n", TestContext.Current.CancellationToken);

            await WaitForConditionAsync(() => updates.Any(u => u.AppendedText.Contains("hello")), cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.Contains(
                updates,
                u => u.HasFileSwitched
                    && string.IsNullOrWhiteSpace(u.CurrentLogFilePath)
                    && string.IsNullOrEmpty(u.AppendedText));
            Assert.Contains(updates, u => u.AppendedText.Contains("hello"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task StartAsync_PreservesMultibyteCodePointsAcrossChunkBoundary()
    {
        // Each emoji below is a 4-byte UTF-8 sequence. Filling the read buffer
        // (4096 bytes) so the boundary lands mid-codepoint must not corrupt the
        // character into a replacement char.
        var line = "emoji \U0001F600 test \U0001F929 end\n";
        var oneLineBytes = Encoding.UTF8.GetByteCount(line);
        var linesPerBuffer = (4096 / oneLineBytes) + 1;
        var fullBlock = string.Concat(Enumerable.Repeat(line, linesPerBuffer));
        // Ensure the full block exceeds one 4096-byte read so a codepoint
        // can straddle the boundary.
        Assert.True(Encoding.UTF8.GetByteCount(fullBlock) > 4096);

        var tempRoot = CreateTempDirectory();
        try
        {
            var logFile = Path.Combine(tempRoot, "app.log");
            await File.WriteAllTextAsync(logFile, fullBlock, TestContext.Current.CancellationToken);

            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(30));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await WaitForConditionAsync(
                () => updates.Any(u => u.AppendedText.Contains("\U0001F600", StringComparison.Ordinal) && u.AppendedText.Contains("\U0001F929", StringComparison.Ordinal)),
                cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            var combined = string.Concat(updates.Select(u => u.AppendedText));
            Assert.DoesNotContain("�", combined); // no replacement chars from split codepoints
            Assert.Contains("\U0001F600", combined);
            Assert.Contains("\U0001F929", combined);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task StartAsync_PreservesMultibyteCodePointsInRecentTail()
    {
        // The recent-tail path reads from an arbitrary byte offset; the start
        // must align to a codepoint boundary so the leading bytes of a split
        // multi-byte sequence are not turned into replacement chars.
        var prefix = string.Concat(Enumerable.Repeat("prefix line\n", 400));
        var suffix = "tail \U0001F600 marker \U0001F929 end\n";
        var tempRoot = CreateTempDirectory();
        try
        {
            var logFile = Path.Combine(tempRoot, "app.log");
            await File.WriteAllTextAsync(logFile, prefix + suffix, TestContext.Current.CancellationToken);

            var updates = new ConcurrentQueue<LiveLogStreamUpdate>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var service = new LiveLogStreamService(pollInterval: TimeSpan.FromMilliseconds(30));

            var runTask = service.StartAsync(tempRoot, update =>
            {
                updates.Enqueue(update);
                return Task.CompletedTask;
            }, cts.Token);

            await WaitForConditionAsync(
                () => updates.Any(u => u.AppendedText.Contains("\U0001F600", StringComparison.Ordinal)),
                cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            var tailUpdate = updates.Last(u => !string.IsNullOrEmpty(u.AppendedText));
            Assert.DoesNotContain("�", tailUpdate.AppendedText);
            Assert.Contains("\U0001F600", tailUpdate.AppendedText);
            Assert.Contains("\U0001F929", tailUpdate.AppendedText);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(20, cancellationToken);
        }
    }
}

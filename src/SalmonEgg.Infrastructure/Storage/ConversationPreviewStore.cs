using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.ConversationPreview;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public class ConversationPreviewStore : IConversationPreviewStore
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<ConversationPreviewStore> _logger;
    private readonly string _previewsDirectory;
    private readonly ConcurrentDictionary<string, PreviewSaveCoordinator> _saveCoordinators = new(StringComparer.Ordinal);
    private int _migrationState;
    private const string PreviewsDirectoryName = "conversation-previews";

    public ConversationPreviewStore(IAppDataService appDataService, ILogger<ConversationPreviewStore> logger)
    {
        _appDataService = appDataService ?? throw new ArgumentNullException(nameof(appDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _previewsDirectory = Path.Combine(_appDataService.AppDataRootPath, PreviewsDirectoryName);
    }

    public async Task<ConversationPreviewSnapshot?> LoadAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return null;

        EnsureMigrated();
        var filePath = GetFilePath(conversationId);
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ConversationPreviewJsonContext.Default.ConversationPreviewSnapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load conversation preview for {ConversationId}", conversationId);
            return null;
        }
    }

    public async Task SaveAsync(ConversationPreviewSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.ConversationId)) throw new ArgumentException("ConversationId cannot be empty", nameof(snapshot));

        EnsureMigrated();
        var filePath = GetFilePath(snapshot.ConversationId);
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var signature = ComputeContentSignature(snapshot);
        var serialized = JsonSerializer.Serialize(snapshot, ConversationPreviewJsonContext.Default.ConversationPreviewSnapshot);
        var coordinator = _saveCoordinators.GetOrAdd(snapshot.ConversationId, static _ => new PreviewSaveCoordinator());
        Task completionTask;
        lock (coordinator.Gate)
        {
            if (string.Equals(signature, coordinator.PendingWrite?.Signature, StringComparison.Ordinal))
            {
                completionTask = coordinator.PendingWriteCompletion?.Task ?? Task.CompletedTask;
            }
            else if (string.Equals(signature, coordinator.LastPersistedSignature, StringComparison.Ordinal))
            {
                completionTask = Task.CompletedTask;
            }
            else
            {
                coordinator.PendingWrite = new PendingPreviewWrite(filePath, serialized, signature);
                coordinator.PendingWriteCompletion ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                completionTask = coordinator.PendingWriteCompletion.Task;

                if (coordinator.DrainTask is null || coordinator.DrainTask.IsCompleted)
                {
                    coordinator.DrainTask = DrainPendingWritesAsync(snapshot.ConversationId, coordinator);
                }
            }
        }

        try
        {
            await AwaitCompletionAsync(completionTask, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return Task.CompletedTask;

        EnsureMigrated();
        var filePath = GetFilePath(conversationId);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete conversation preview for {ConversationId}", conversationId);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string conversationId)
    {
        // Use a safe, collision-resistant filename:
        // 1. Compute a short hash prefix for uniqueness guarantees
        // 2. Sanitize the original ID for human-readable fallback
        // 3. Truncate to avoid MAX_PATH issues on Windows
        var safeId = new StringBuilder(conversationId.Length);
        foreach (var c in conversationId)
        {
            safeId.Append(IsSafeFileNameChar(c) ? c : '_');
        }

        // Compute 8-char hex hash prefix for collision resistance
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(conversationId));
        var hashHex = ToHex(hashBytes, 4); // First 4 bytes = 8 hex chars

        // Combine: hash prefix + sanitized-truncated ID
        // Max total filename length: 8 (hash) + 1 (dash) + 64 (truncated id) + 5 (.json) = 78 chars
        var displayName = safeId.Length > 64 ? safeId.ToString(0, 64) : safeId.ToString();
        var fileName = $"{hashHex}-{displayName}.json";

        return Path.Combine(_previewsDirectory, fileName);
    }

    private static bool IsSafeFileNameChar(char c)
    {
        // Exclude both OS-invalid chars and characters that could enable
        // path traversal or other filesystem attacks.
        if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
            return false;
        if (c == '.' || c == '/' || c == '\\' || c == ':')
            return false;
        // Keep alphanumerics, dashes, underscores, and common Unicode
        return char.IsLetterOrDigit(c) || c == '-' || c == '_';
    }

    private void EnsureMigrated()
    {
        if (Interlocked.Exchange(ref _migrationState, 1) == 1)
        {
            return;
        }

        try
        {
            MigrateOldNamingScheme();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up old-format preview cache files.");
        }
    }

    private void MigrateOldNamingScheme()
    {
        if (!Directory.Exists(_previewsDirectory))
            return;

        foreach (var file in Directory.GetFiles(_previewsDirectory, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // New naming scheme: "XXXXXXXX-..." (8 hex chars + dash prefix)
            // Old naming scheme: no such prefix
            if (name.Length < 9 || name[8] != '-' || !IsAllHex(name.AsSpan(0, 8)))
            {
                try
                {
                    File.Delete(file);
                    _logger.LogDebug("Deleted orphaned preview cache file: {FileName}", Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete orphaned preview file: {FileName}", Path.GetFileName(file));
                }
            }
        }
    }

    private static string ToHex(byte[] bytes, int count)
    {
        const string HexChars = "0123456789ABCDEF";
        var sb = new StringBuilder(count * 2);
        for (var i = 0; i < count; i++)
        {
            sb.Append(HexChars[bytes[i] >> 4]);
            sb.Append(HexChars[bytes[i] & 0xF]);
        }
        return sb.ToString();
    }

    private static bool IsAllHex(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    private async Task DrainPendingWritesAsync(string conversationId, PreviewSaveCoordinator coordinator)
    {
        while (true)
        {
            PendingPreviewWrite? write;
            TaskCompletionSource<object?>? completion;
            lock (coordinator.Gate)
            {
                write = coordinator.PendingWrite;
                completion = coordinator.PendingWriteCompletion;
                coordinator.PendingWrite = null;
                coordinator.PendingWriteCompletion = null;
                if (write is null || completion is null)
                {
                    coordinator.DrainTask = null;
                    return;
                }
            }

            try
            {
                await AtomicFile.WriteUtf8AtomicAsync(write.Path, write.Serialized).ConfigureAwait(false);
                lock (coordinator.Gate)
                {
                    coordinator.LastPersistedSignature = write.Signature;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save conversation preview for {ConversationId}", conversationId);
            }
            finally
            {
                completion.TrySetResult(null);
            }
        }
    }

    private static string ComputeContentSignature(ConversationPreviewSnapshot snapshot)
    {
        var hash = new HashCode();
        hash.Add(snapshot.ConversationId, StringComparer.Ordinal);
        hash.Add(snapshot.Entries.Count);

        foreach (var entry in snapshot.Entries)
        {
            hash.Add(entry.Sender, StringComparer.Ordinal);
            hash.Add(entry.Text, StringComparer.Ordinal);
            hash.Add(entry.Timestamp.UtcTicks);
        }

        return hash.ToHashCode().ToString("X8");
    }

    private static async Task AwaitCompletionAsync(Task completionTask, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await completionTask.ConfigureAwait(false);
            return;
        }

        var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
        var finishedTask = await Task.WhenAny(completionTask, cancellationTask).ConfigureAwait(false);
        if (finishedTask == cancellationTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await completionTask.ConfigureAwait(false);
    }

    private sealed class PreviewSaveCoordinator
    {
        public object Gate { get; } = new();
        public PendingPreviewWrite? PendingWrite { get; set; }
        public TaskCompletionSource<object?>? PendingWriteCompletion { get; set; }
        public Task? DrainTask { get; set; }
        public string? LastPersistedSignature { get; set; }
    }

    private sealed record PendingPreviewWrite(
        string Path,
        string Serialized,
        string Signature);
}

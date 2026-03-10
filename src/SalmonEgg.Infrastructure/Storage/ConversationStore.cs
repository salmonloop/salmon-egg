using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAppDataService _appData;

    public ConversationStore(IAppDataService appData)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
    }

    public async Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetDocumentPath();
            if (!File.Exists(path))
            {
                return new ConversationDocument();
            }

            await using var stream = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync<ConversationDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return doc ?? new ConversationDocument();
        }
        catch
        {
            // Corrupted file or schema changes: do not crash the app, just start fresh.
            return new ConversationDocument();
        }
    }

    public async Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var path = GetDocumentPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        // Atomic replace best-effort (cross-platform).
        try
        {
            File.Copy(tmp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private string GetDocumentPath()
    {
        return Path.Combine(_appData.AppDataRootPath, "conversations", "conversations.v1.json");
    }
}


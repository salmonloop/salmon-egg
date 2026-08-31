using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConversationStore : IConversationStore
{
    private static readonly JsonTypeInfo<ConversationDocument> ConversationDocumentJsonType =
        ConversationJsonContext.Default.ConversationDocument;

    private readonly IAppDataService _appData;
    private readonly ILogger<ConversationStore> _logger;

    public ConversationStore(IAppDataService appData, ILogger<ConversationStore> logger)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = GetDocumentPath();
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new ConversationDocument();
        }
        catch (DirectoryNotFoundException)
        {
            return new ConversationDocument();
        }

        try
        {
            return JsonSerializer.Deserialize(json, ConversationDocumentJsonType) ?? new ConversationDocument();
        }
        catch (JsonException ex)
        {
            // A corrupted document can never become readable again, so quarantine it for
            // manual recovery and start fresh. Transient failures (IO, cancellation) propagate
            // instead: reporting an empty document there would let the next save overwrite the
            // real history.
            QuarantineCorruptedDocument(path, ex);
            return new ConversationDocument();
        }
    }

    public async Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var json = JsonSerializer.Serialize(document, ConversationDocumentJsonType);
        await AtomicFile.WriteUtf8AtomicAsync(GetDocumentPath(), json, cancellationToken).ConfigureAwait(false);
    }

    private void QuarantineCorruptedDocument(string path, JsonException error)
    {
        var quarantinePath = path + ".corrupt";
        try
        {
            File.Move(path, quarantinePath, overwrite: true);
            _logger.LogWarning(
                error,
                "Conversation document is corrupted; quarantined to {QuarantinePath} and starting fresh",
                quarantinePath);
        }
        catch (Exception moveError)
        {
            _logger.LogWarning(
                moveError,
                "Conversation document is corrupted and quarantine failed; starting fresh (DocumentPath={DocumentPath})",
                path);
        }
    }

    private string GetDocumentPath()
    {
        return Path.Combine(_appData.AppDataRootPath, "conversations", "conversations.v1.json");
    }
}

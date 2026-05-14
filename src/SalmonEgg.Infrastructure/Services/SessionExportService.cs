using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class SessionExportService : ISessionExportService
{
    private readonly IAppDataService _paths;
    private readonly IAppFileStore _fileStore;

    public SessionExportService(IAppDataService paths, IAppFileStore fileStore)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
    }

    public async Task<string> ExportAsync(SessionExportRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var format = string.Equals(request.Format, "json", StringComparison.OrdinalIgnoreCase) ? "json" : "md";
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "no-session" : request.SessionId;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = SanitizeFileName($"session-{sessionId}-{timestamp}.{format}");
        var path = Path.Combine(_paths.ExportsDirectoryPath, fileName);
        var content = format == "json" ? BuildJson(request) : BuildMarkdown(request);

        await _fileStore.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string BuildJson(SessionExportRequest request)
    {
        var payload = new SessionExportPayload(
            request.SessionId,
            request.AgentName,
            request.AgentVersion,
            DateTimeOffset.UtcNow,
            request.Messages.Select(m => new SessionExportPayloadMessage(
                m.Id,
                m.Timestamp,
                m.IsOutgoing,
                m.ContentType,
                m.Title,
                m.Text)).ToList());

        return System.Text.Json.JsonSerializer.Serialize(
            payload,
            SessionExportJsonContext.Default.SessionExportPayload);
    }

    private static string BuildMarkdown(SessionExportRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Session Export");
        sb.AppendLine();
        sb.AppendLine($"- SessionId: `{request.SessionId}`");
        sb.AppendLine($"- Agent: `{request.AgentName}` `{request.AgentVersion}`");
        sb.AppendLine($"- ExportedAt(UTC): `{DateTimeOffset.UtcNow:O}`");
        sb.AppendLine();

        foreach (var message in request.Messages)
        {
            var who = message.IsOutgoing ? "User" : "Agent";
            sb.AppendLine($"## {who} · {message.Timestamp:O}");
            if (!string.IsNullOrWhiteSpace(message.Title))
            {
                sb.AppendLine();
                sb.AppendLine($"**{message.Title}**");
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                sb.AppendLine();
                sb.AppendLine(message.Text);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}

internal sealed record SessionExportPayload(
    string? SessionId,
    string? AgentName,
    string? AgentVersion,
    DateTimeOffset ExportedAtUtc,
    System.Collections.Generic.List<SessionExportPayloadMessage> Messages);

internal sealed partial record SessionExportPayloadMessage(
    string Id,
    DateTimeOffset Timestamp,
    bool IsOutgoing,
    string? ContentType,
    string? Title,
    string? Text);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionExportPayload))]
internal partial class SessionExportJsonContext : JsonSerializerContext
{
}

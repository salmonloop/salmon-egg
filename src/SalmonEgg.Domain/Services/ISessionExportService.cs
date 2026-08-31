using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Domain.Services;

public interface ISessionExportService
{
    Task<SessionExportResult> ExportAsync(SessionExportRequest request, CancellationToken cancellationToken = default);
}

public enum SessionExportStatus
{
    Success,
    Unsupported
}

public sealed record SessionExportResult(SessionExportStatus Status, string? Path)
{
    public static SessionExportResult Success(string path) => new(SessionExportStatus.Success, path);

    public static SessionExportResult Unsupported() => new(SessionExportStatus.Unsupported, null);
}

public sealed record SessionExportRequest(
    string Format,
    string? SessionId,
    string? AgentName,
    string? AgentVersion,
    IReadOnlyList<SessionExportMessage> Messages);

public sealed record SessionExportMessage(
    string Id,
    // null when the source message had no authoritative time (ACP replay/chunks). Exports must
    // not synthesize a clock; the renderer omits the timestamp segment when null.
    DateTimeOffset? Timestamp,
    bool IsOutgoing,
    string? ContentType,
    string? Title,
    string? Text);

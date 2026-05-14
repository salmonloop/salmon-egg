using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Domain.Services;

public interface ISessionExportService
{
    Task<string> ExportAsync(SessionExportRequest request, CancellationToken cancellationToken = default);
}

public sealed record SessionExportRequest(
    string Format,
    string? SessionId,
    string? AgentName,
    string? AgentVersion,
    IReadOnlyList<SessionExportMessage> Messages);

public sealed record SessionExportMessage(
    string Id,
    DateTimeOffset Timestamp,
    bool IsOutgoing,
    string? ContentType,
    string? Title,
    string? Text);

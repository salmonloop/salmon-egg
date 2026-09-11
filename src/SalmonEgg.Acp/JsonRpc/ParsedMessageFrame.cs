using System.Collections.Generic;

namespace SalmonEgg.Acp.JsonRpc;

internal sealed record ParsedMessageFrame(
    bool IsBatch,
    bool IsResponseBatch,
    IReadOnlyList<ParsedMessageItem> Items);

internal sealed record ParsedMessageItem(
    JsonRpcMessage? Message,
    string? Error = null,
    bool IsResponse = false);

using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Localization;

/// <summary>
/// Single mapping from ACP transport types to CoreStrings resource keys and invariant
/// fallback labels. UI converters and option factories should reuse this instead of
/// hard-coding display text.
/// </summary>
public static class AcpTransportLocalization
{
    public const string StdioResourceKey = "AcpConnection_TransportStdio";
    public const string WebSocketResourceKey = "AcpConnection_TransportWebSocket";
    public const string HttpSseResourceKey = "AcpConnection_TransportHttpSse";

    public static string ResolveResourceKey(TransportType transport)
        => transport switch
        {
            TransportType.Stdio => StdioResourceKey,
            TransportType.HttpSse => HttpSseResourceKey,
            _ => WebSocketResourceKey
        };

    public static string ResolveInvariantFallback(TransportType transport)
        => transport switch
        {
            TransportType.Stdio => "Stdio (subprocess)",
            TransportType.HttpSse => "Streamable HTTP",
            _ => "WebSocket"
        };

    public static bool TryResolveTransport(object? value, out TransportType transport)
    {
        switch (value)
        {
            case TransportType typed:
                transport = typed;
                return true;
            case ServerConfiguration configuration:
                transport = configuration.Transport;
                return true;
            case string text when Enum.TryParse(text, ignoreCase: true, out TransportType parsed):
                transport = parsed;
                return true;
            default:
                transport = default;
                return false;
        }
    }
}

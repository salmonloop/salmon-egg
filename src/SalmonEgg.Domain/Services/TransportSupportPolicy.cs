using System;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public sealed class TransportSupportPolicy : ITransportSupportPolicy
{
    private readonly IPlatformCapabilityService _capabilities;

    public TransportSupportPolicy(IPlatformCapabilityService capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public TransportType DefaultTransport =>
        _capabilities.SupportsStdioTransport ? TransportType.Stdio : TransportType.WebSocket;

    public bool IsSupported(TransportType transport)
        => transport switch
        {
            TransportType.Stdio => _capabilities.SupportsStdioTransport,
            TransportType.WebSocket => true,
            TransportType.HttpSse => true,
            _ => false
        };

    public TransportType Coerce(TransportType requested)
        => IsSupported(requested) ? requested : DefaultTransport;

    public string? GetUnsupportedReason(TransportType transport)
        => transport switch
        {
            TransportType.Stdio when !_capabilities.SupportsStdioTransport =>
                "Stdio transport requires a desktop process host and is not supported on this platform.",
            TransportType.WebSocket or TransportType.HttpSse => null,
            _ => $"Unsupported transport type: {transport}."
        };
}

using System;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Infrastructure.Client;

public sealed class EndpointValidatingTransportFactory : ITransportFactory
{
    private readonly ITransportFactory _inner;
    private readonly ITransportEndpointAccessPolicy _endpointAccessPolicy;

    public EndpointValidatingTransportFactory(
        ITransportFactory inner,
        ITransportEndpointAccessPolicy endpointAccessPolicy)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _endpointAccessPolicy = endpointAccessPolicy ?? throw new ArgumentNullException(nameof(endpointAccessPolicy));
    }

    public ITransport CreateTransport(
        TransportType transportType,
        string? command = null,
        string? args = null,
        string? url = null)
    {
        if (transportType is TransportType.WebSocket or TransportType.HttpSse)
        {
            var result = _endpointAccessPolicy.Validate(transportType, url);
            if (!result.IsAllowed)
            {
                throw new NotSupportedException(
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? $"Transport endpoint is not supported on this platform. transport={transportType} endpoint={url}"
                        : result.ErrorMessage);
            }
        }

        return _inner.CreateTransport(transportType, command, args, url);
    }
}

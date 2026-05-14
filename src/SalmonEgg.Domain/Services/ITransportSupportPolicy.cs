using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public interface ITransportSupportPolicy
{
    TransportType DefaultTransport { get; }

    bool IsSupported(TransportType transport);

    TransportType Coerce(TransportType requested);

    string? GetUnsupportedReason(TransportType transport);
}

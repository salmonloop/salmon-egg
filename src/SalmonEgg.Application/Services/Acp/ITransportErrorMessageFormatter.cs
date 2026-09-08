using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Application.Services.Acp;

/// <summary>
/// Lets a host present transport failures before the SDK's string-only error boundary.
/// </summary>
public interface ITransportErrorMessageFormatter
{
    string Format(TransportErrorEventArgs error);
}

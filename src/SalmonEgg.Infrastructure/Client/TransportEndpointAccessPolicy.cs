using System;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Infrastructure.Client;

/// <summary>
/// Runtime facts that affect whether a transport endpoint can be reached from the current host.
/// </summary>
public interface ITransportEndpointAccessContext
{
    /// <summary>
    /// Gets whether the app is running inside a browser host.
    /// </summary>
    bool IsBrowserHosted { get; }

    /// <summary>
    /// Gets whether the current browser origin is secure, such as HTTPS.
    /// </summary>
    bool IsSecureOrigin { get; }
}

/// <summary>
/// Validates whether a transport endpoint can be used from the current runtime host.
/// </summary>
public interface ITransportEndpointAccessPolicy
{
    /// <summary>
    /// Validates the endpoint for the specified transport.
    /// </summary>
    TransportEndpointAccessResult Validate(TransportType transport, string? endpoint);
}

/// <summary>
/// Endpoint access validation result.
/// </summary>
public sealed record TransportEndpointAccessResult(bool IsAllowed, string? ErrorMessage)
{
    /// <summary>
    /// A successful endpoint access result.
    /// </summary>
    public static TransportEndpointAccessResult Allowed { get; } = new(true, null);
}

/// <summary>
/// Default non-browser endpoint access context.
/// </summary>
public sealed class DefaultTransportEndpointAccessContext : ITransportEndpointAccessContext
{
    /// <inheritdoc />
    public bool IsBrowserHosted => false;

    /// <inheritdoc />
    public bool IsSecureOrigin => false;
}

/// <summary>
/// Applies host-specific endpoint access rules before a transport is created.
/// </summary>
public sealed class TransportEndpointAccessPolicy : ITransportEndpointAccessPolicy
{
    private readonly ITransportEndpointAccessContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportEndpointAccessPolicy"/> class.
    /// </summary>
    public TransportEndpointAccessPolicy(ITransportEndpointAccessContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public TransportEndpointAccessResult Validate(TransportType transport, string? endpoint)
    {
        if (transport != TransportType.WebSocket
            || !_context.IsBrowserHosted
            || !_context.IsSecureOrigin
            || string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
        {
            return TransportEndpointAccessResult.Allowed;
        }

        return new TransportEndpointAccessResult(
            false,
            "Browser HTTPS pages cannot connect to ws:// WebSocket endpoints. "
            + "Use a wss:// WebSocket endpoint for this ACP profile or run the app from an HTTP origin for local testing. "
            + $"endpoint={endpoint.Trim()}");
    }
}

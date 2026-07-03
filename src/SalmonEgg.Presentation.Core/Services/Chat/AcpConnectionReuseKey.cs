using System;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpConnectionReuseKey(
    TransportType TransportType,
    string StdioCommand,
    string StdioArgumentsCanonical,
    string RemoteUrl)
{
    public static AcpConnectionReuseKey FromTransportConfiguration(IAcpTransportConfiguration transportConfiguration)
    {
        ArgumentNullException.ThrowIfNull(transportConfiguration);

        var normalizedCommand = (transportConfiguration.StdioCommand ?? string.Empty).Trim();
        var normalizedUrl = (transportConfiguration.RemoteUrl ?? string.Empty).Trim();
        var canonicalArgs = StdioCommandLine.CanonicalizeArguments(transportConfiguration.StdioArguments);

        return new AcpConnectionReuseKey(
            transportConfiguration.SelectedTransportType,
            normalizedCommand,
            canonicalArgs,
            normalizedUrl);
    }
}

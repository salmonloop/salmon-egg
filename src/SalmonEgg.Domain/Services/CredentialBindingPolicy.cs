using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Owns the persisted binding vocabulary and destination identity; it never reads a credential.
/// </summary>
public static class CredentialBindingPolicy
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Content-Length", "Content-Type", "Accept", "Upgrade", "Transfer-Encoding",
        "TE", "Trailer", "Keep-Alive", "Proxy-Authorization", "Proxy-Connection", "Cookie", "Set-Cookie",
    };

    public static CredentialBinding Create(
        ServerConfiguration configuration,
        CredentialSource source,
        CredentialTarget target,
        string name,
        string? scheme = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new CredentialBinding(source, target, name, scheme, GetTargetIdentity(configuration));
    }

    public static string? GetValidationError(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var binding = configuration.CredentialBinding;
        if (binding is null)
        {
            return null;
        }

        if (!Enum.IsDefined(binding.Source) || !Enum.IsDefined(binding.Target))
        {
            return "The credential binding source or target is unsupported. Choose token or API key and an environment variable or header.";
        }

        if (!string.Equals(binding.TargetIdentity, GetTargetIdentity(configuration), StringComparison.Ordinal))
        {
            return "The profile destination changed. Bind the credential again to approve its new destination.";
        }

        if (binding.Target == CredentialTarget.Environment)
        {
            return GetEnvironmentBindingError(configuration, binding);
        }

        return GetHeaderBindingError(configuration, binding);
    }

    public static bool MatchesEndpoint(Uri expected, Uri actual)
        => string.Equals(expected.AbsoluteUri, actual.AbsoluteUri, StringComparison.Ordinal);

    private static string GetTargetIdentity(ServerConfiguration configuration)
    {
        var parts = new List<string> { ((int)configuration.Transport).ToString(CultureInfo.InvariantCulture) };
        if (configuration.Transport == TransportType.Stdio)
        {
            parts.Add((configuration.StdioCommand ?? string.Empty).Trim());
            parts.Add(StdioCommandLine.CanonicalizeArguments(configuration.StdioArguments));
            foreach (var pair in configuration.StdioEnvironment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                parts.Add(pair.Key);
                parts.Add(pair.Value);
            }
        }
        else
        {
            parts.Add(Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var endpoint)
                ? endpoint.AbsoluteUri
                : configuration.ServerUrl ?? string.Empty);
        }

        var canonical = StdioCommandLine.CanonicalizeArguments(parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? GetEnvironmentBindingError(ServerConfiguration configuration, CredentialBinding binding)
    {
        if (configuration.Transport != TransportType.Stdio || string.IsNullOrWhiteSpace(configuration.StdioCommand))
        {
            return "An environment credential requires a configured stdio command.";
        }

        if (string.IsNullOrWhiteSpace(binding.Name) || binding.Name != binding.Name.Trim()
            || binding.Name.Contains('=') || binding.Name.Any(char.IsControl))
        {
            return "Choose a non-empty environment variable name without '=' or null characters.";
        }

        if (string.Equals(binding.Name, "PATH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(binding.Name, "PATHEXT", StringComparison.OrdinalIgnoreCase))
        {
            return "The launcher owns PATH and PATHEXT. Choose the agent's credential environment variable.";
        }

        if (!string.IsNullOrEmpty(binding.Scheme))
        {
            return "An environment credential has no header scheme. Remove the scheme.";
        }

        return null;
    }

    private static string? GetHeaderBindingError(ServerConfiguration configuration, CredentialBinding binding)
    {
        if (configuration.Transport is not (TransportType.WebSocket or TransportType.StreamableHttp)
            || !Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var endpoint)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || (configuration.Transport == TransportType.WebSocket && endpoint.Scheme is not ("ws" or "wss"))
            || (configuration.Transport == TransportType.StreamableHttp && endpoint.Scheme is not ("http" or "https")))
        {
            return "A header credential requires a matching HTTP or WebSocket endpoint without user information or a fragment.";
        }

        if (!IsHttpToken(binding.Name) || ReservedHeaders.Contains(binding.Name)
            || binding.Name.StartsWith("Acp-", StringComparison.OrdinalIgnoreCase)
            || binding.Name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose an authentication header name, not a transport-controlled header.";
        }

        if (!string.IsNullOrEmpty(binding.Scheme) && !IsHttpToken(binding.Scheme))
        {
            return "Use one HTTP token for the header scheme, or leave it empty for a raw value.";
        }

        return null;
    }

    private static bool IsHttpToken(string? value)
        => !string.IsNullOrEmpty(value) && value.All(character => char.IsAsciiLetterOrDigit(character)
            || "!#$%&'*+-.^_`|~".Contains(character));
}

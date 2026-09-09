using System;
using System.Net.Http;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Network;

internal static class BoundCredentialHeader
{
    internal static void EnsureEndpoint(ResolvedCredentialBinding? credential, Uri endpoint)
    {
        if (credential is { HasHeader: true }
            && !CredentialBindingPolicy.MatchesEndpoint(credential.Endpoint!, endpoint))
        {
            throw new InvalidOperationException("The connection destination differs from its credential binding. Recreate the connection from the updated profile.");
        }
    }

    internal static void Apply(ResolvedCredentialBinding? credential, HttpRequestMessage request)
    {
        if (credential is not { HasHeader: true })
        {
            return;
        }

        EnsureEndpoint(credential, request.RequestUri!);
        // Resolver has validated the field name and every value byte. Avoid value parsers for known
        // headers: their FormatException can include the credential in its diagnostic message.
        if (!request.Headers.TryAddWithoutValidation(credential.HeaderName!, credential.HeaderValue!))
        {
            throw new InvalidOperationException("The authentication header is not supported by this transport.");
        }
    }
}

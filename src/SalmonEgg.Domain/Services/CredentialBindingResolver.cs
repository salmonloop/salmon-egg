using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Resolves only the already hydrated configuration snapshot. Secure storage and persistence retain
/// their existing owner; transport creation performs no hidden reads or writes.
/// </summary>
public static class CredentialBindingResolver
{
    public static CredentialBindingResolution Resolve(ServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (CredentialBindingPolicy.GetValidationError(configuration) is { } error)
        {
            return CredentialBindingResolution.Failure(error);
        }

        var environment = new Dictionary<string, string>(configuration.StdioEnvironment, StringComparer.Ordinal);
        var binding = configuration.CredentialBinding;
        if (binding is null)
        {
            return CredentialBindingResolution.Success(new ResolvedCredentialBinding(
                new ReadOnlyDictionary<string, string>(environment)));
        }

        var secret = binding.Source == CredentialSource.Token
            ? configuration.Authentication?.Token
            : configuration.Authentication?.ApiKey;
        if (string.IsNullOrEmpty(secret))
        {
            return CredentialBindingResolution.Failure(
                "The bound credential is not set. Set it in secure storage or remove the credential binding before connecting.");
        }

        if (secret.Contains('\0') || (binding.Target == CredentialTarget.Header && secret.Any(character => character < ' ' || character > '~')))
        {
            return CredentialBindingResolution.Failure("The credential contains characters unsupported by its destination. Replace the stored credential.");
        }

        if (binding.Target == CredentialTarget.Environment)
        {
            // Windows environment names are case-insensitive. Remove aliases on all platforms so the
            // child receives one unambiguous value without putting OS checks in the domain layer.
            foreach (var key in environment.Keys.Where(key => string.Equals(key, binding.Name, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                environment.Remove(key);
            }

            environment[binding.Name] = secret;
            return CredentialBindingResolution.Success(new ResolvedCredentialBinding(
                new ReadOnlyDictionary<string, string>(environment)));
        }

        var headerValue = string.IsNullOrEmpty(binding.Scheme) ? secret : binding.Scheme + " " + secret;
        return CredentialBindingResolution.Success(new ResolvedCredentialBinding(
            new ReadOnlyDictionary<string, string>(environment),
            new Uri(configuration.ServerUrl), binding.Name, headerValue));
    }
}

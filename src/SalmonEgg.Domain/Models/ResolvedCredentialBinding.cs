using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Domain.Models;

/// <summary>
/// A connection-scoped snapshot. Deliberately not a record: diagnostic formatting must not reveal values.
/// </summary>
public sealed class ResolvedCredentialBinding
{
    internal ResolvedCredentialBinding(
        IReadOnlyDictionary<string, string> environment,
        Uri? endpoint = null,
        string? headerName = null,
        string? headerValue = null)
    {
        Environment = environment;
        Endpoint = endpoint;
        HeaderName = headerName;
        HeaderValue = headerValue;
    }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public Uri? Endpoint { get; }

    public string? HeaderName { get; }

    public string? HeaderValue { get; }

    public bool HasHeader => HeaderName is not null;
}

public sealed class CredentialBindingResolution
{
    private CredentialBindingResolution(ResolvedCredentialBinding? value, CredentialBindingValidationError? errorKind)
    {
        Value = value;
        ErrorKind = errorKind;
    }

    public ResolvedCredentialBinding? Value { get; }

    /// <summary>Stable failure category for a host's localized presentation.</summary>
    public CredentialBindingValidationError? ErrorKind { get; }

    /// <summary>English diagnostic for non-UI callers; never contains the credential value.</summary>
    public string? Error => ErrorKind is { } error ? CredentialBindingPolicy.GetDiagnosticMessage(error) : null;

    public bool IsSuccess => Value is not null;

    internal static CredentialBindingResolution Success(ResolvedCredentialBinding value) => new(value, null);

    internal static CredentialBindingResolution Failure(CredentialBindingValidationError error) => new(null, error);
}

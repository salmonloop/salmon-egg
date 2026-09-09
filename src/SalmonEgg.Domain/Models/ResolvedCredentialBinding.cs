using System;
using System.Collections.Generic;

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
    private CredentialBindingResolution(ResolvedCredentialBinding? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public ResolvedCredentialBinding? Value { get; }

    public string? Error { get; }

    public bool IsSuccess => Value is not null;

    internal static CredentialBindingResolution Success(ResolvedCredentialBinding value) => new(value, null);

    internal static CredentialBindingResolution Failure(string error) => new(null, error);
}

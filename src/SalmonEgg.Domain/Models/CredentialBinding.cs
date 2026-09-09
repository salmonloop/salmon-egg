namespace SalmonEgg.Domain.Models;

/// <summary>
/// Non-secret instructions binding one stored credential to the profile's explicitly selected target.
/// </summary>
public sealed record CredentialBinding(
    CredentialSource Source,
    CredentialTarget Target,
    string Name,
    string? Scheme,
    string TargetIdentity);

public enum CredentialSource
{
    Token,
    ApiKey,
}

public enum CredentialTarget
{
    Environment,
    Header,
}

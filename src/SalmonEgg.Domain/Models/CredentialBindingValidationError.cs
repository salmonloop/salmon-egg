namespace SalmonEgg.Domain.Models;

/// <summary>
/// Classifies binding failures independently of a user interface or diagnostic language.
/// </summary>
public enum CredentialBindingValidationError
{
    UnsupportedBinding,
    DestinationChanged,
    EnvironmentRequiresStdio,
    InvalidEnvironmentName,
    ReservedEnvironmentName,
    EnvironmentHasScheme,
    InvalidHeaderEndpoint,
    InvalidHeaderName,
    InvalidHeaderScheme,
    MissingCredential,
    UnsupportedCredentialCharacters,
}

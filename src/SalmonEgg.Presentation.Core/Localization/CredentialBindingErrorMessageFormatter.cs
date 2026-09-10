using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Localization;

public static class CredentialBindingErrorMessageFormatter
{
    public static string Format(CredentialBindingValidationError error, IStringLocalizer<CoreStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var resourceKey = error switch
        {
            CredentialBindingValidationError.UnsupportedBinding => "CredentialBinding_UnsupportedBinding",
            CredentialBindingValidationError.DestinationChanged => "CredentialBinding_DestinationChanged",
            CredentialBindingValidationError.EnvironmentRequiresStdio => "CredentialBinding_EnvironmentRequiresStdio",
            CredentialBindingValidationError.InvalidEnvironmentName => "CredentialBinding_InvalidEnvironmentName",
            CredentialBindingValidationError.ReservedEnvironmentName => "CredentialBinding_ReservedEnvironmentName",
            CredentialBindingValidationError.EnvironmentHasScheme => "CredentialBinding_EnvironmentHasScheme",
            CredentialBindingValidationError.InvalidHeaderEndpoint => "CredentialBinding_InvalidHeaderEndpoint",
            CredentialBindingValidationError.InvalidHeaderName => "CredentialBinding_InvalidHeaderName",
            CredentialBindingValidationError.InvalidHeaderScheme => "CredentialBinding_InvalidHeaderScheme",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
        };
        return localizer[resourceKey];
    }
}

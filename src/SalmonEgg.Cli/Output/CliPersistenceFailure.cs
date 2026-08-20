using System;
using SalmonEgg.Cli.Hosting;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli.Output;

/// <summary>
/// Renders a configuration persistence failure for the CLI, adding host-specific remediation.
/// </summary>
/// <remarks>
/// The domain message states what happened to the configuration; it deliberately says nothing about
/// command-line flags. The fail-closed credential default is a CLI decision, so the CLI is the layer that
/// owes the operator the way out of it.
/// </remarks>
internal static class CliPersistenceFailure
{
    public static string Describe(ConfigurationPersistenceException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Reason == ConfigurationPersistenceFailureReason.SecureStorageUnavailable
            ? exception.UserMessage
              + $" Credentials are not written unprotected by default; pass {CliSecureStorageOption.Name} to allow plaintext storage."
            : exception.UserMessage;
    }
}

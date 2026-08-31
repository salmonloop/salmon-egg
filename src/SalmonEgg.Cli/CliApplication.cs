using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Validators;
using SalmonEgg.Cli.Commands;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Commands.Credentials;
using SalmonEgg.Cli.Hosting;
using SalmonEgg.Cli.Output;
using SalmonEgg.Infrastructure.Desktop.DependencyInjection;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli;

/// <summary>
/// Hosts one CLI invocation and owns the process boundary around parsing and command execution.
/// </summary>
public static class CliApplication
{
    /// <summary>
    /// Builds the desktop service provider used by the CLI host.
    /// </summary>
    /// <remarks>
    /// This method is internal so tests can verify that the CLI resolves the shared configuration
    /// composition root without invoking a command that reads or writes user data.
    /// </remarks>
    internal static ServiceProvider CreateServiceProvider(ICliOutput output, ICliInput? input = null)
        => CreateServiceProvider(
            output,
            input,
            new CliFallbackSecureStorageWarningState(),
            SecureStorageDowngradePolicy.FailClosed);

    private static ServiceProvider CreateServiceProvider(
        ICliOutput output,
        ICliInput? input,
        CliFallbackSecureStorageWarningState fallbackWarningState,
        SecureStorageDowngradePolicy secureStorageDowngradePolicy)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (fallbackWarningState is null) throw new ArgumentNullException(nameof(fallbackWarningState));

        var services = new ServiceCollection();
        services.AddSingleton(output);
        services.AddSingleton<ICliInput>(input ?? new TextCliInput(Console.In));
        services.AddSingleton(_ => new CliVersionProvider(typeof(CliApplication).Assembly));
        services.AddSingleton<CliCommandFactory>();
        services.AddSingleton<ServerConfigurationHandler>();
        services.AddSingleton<CredentialsHandler>();
        services.AddSingleton<AppSettingsHandler>();
        services.AddSingleton<ConfigPackageHandler>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<FallbackSecureStorage>>(fallbackWarningState);
        services.AddSalmonEggDesktopConfiguration(secureStorageDowngradePolicy);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Parses and invokes one CLI command line.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="stdout">The normal output stream.</param>
    /// <param name="stderr">The diagnostic output stream.</param>
    /// <param name="cancellationToken">The invocation cancellation token.</param>
    /// <returns>The stable process exit code.</returns>
    public static Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
        => RunAsync(args, stdout, stderr, Console.In, cancellationToken);

    public static Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        TextReader stdin,
        CancellationToken cancellationToken = default)
        => RunAsyncCore(args, stdout, stderr, stdin, cancellationToken, null, null);

    /// <summary>
    /// Invokes the CLI with explicit host seams for unit tests.
    /// </summary>
    internal static Task<int> RunAsyncForTesting(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<IServiceProvider, CliCommandFactory>? commandFactoryResolver,
        ICliOutput? outputOverride,
        CancellationToken cancellationToken = default)
        => RunAsyncCore(args, stdout, stderr, Console.In, cancellationToken, commandFactoryResolver, outputOverride);

    private static async Task<int> RunAsyncCore(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        TextReader stdin,
        CancellationToken cancellationToken,
        Func<IServiceProvider, CliCommandFactory>? commandFactoryResolver,
        ICliOutput? outputOverride)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (stdout is null) throw new ArgumentNullException(nameof(stdout));
        if (stderr is null) throw new ArgumentNullException(nameof(stderr));
        if (stdin is null) throw new ArgumentNullException(nameof(stdin));

        var output = outputOverride ?? new TextCliOutput(stdout, stderr);

        // Handled before the container exists and before startup recovery runs. This mode is invoked by
        // the user's own login shell while the app is starting, purely to report the environment that
        // shell produced; building the container or recovering transactions would turn an environment
        // probe into a source of configuration side effects. See CliPrintEnvironment.
        if (CliPrintEnvironment.TryGetMarker(args, out var environmentMarker))
        {
            try
            {
                return await CliPrintEnvironment
                    .WriteAsync(environmentMarker, stdout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CliExitCodes.Failure;
            }
        }

        try
        {
            var fallbackWarningState = new CliFallbackSecureStorageWarningState();
            // The secure-storage policy has to be known before the container is built, because the
            // container owns the store. The command tree cannot be parsed that early — it needs the
            // handlers the container provides — so the flag is read from raw arguments here and then
            // reconciled against the parser below.
            var bootstrapDowngradePolicy = CliSecureStorageOption.ResolveBootstrapPolicy(args);
            await using var services = CreateServiceProvider(
                output,
                new TextCliInput(stdin),
                fallbackWarningState,
                bootstrapDowngradePolicy);
            try
            {
                var commandFactory = commandFactoryResolver is null
                    ? services.GetRequiredService<CliCommandFactory>()
                    : commandFactoryResolver(services);
                var root = commandFactory.CreateRootCommand(args);
                var parseResult = root.Parse(args);

                if (args.Length == 0)
                {
                    var helpParseResult = root.Parse(["--help"]);
                    var helpConfiguration = CreateInvocationConfiguration(stdout, stderr);
                    _ = await helpParseResult.InvokeAsync(helpConfiguration, cancellationToken).ConfigureAwait(false);
                    return CliExitCodes.Success;
                }

                if (ContainsLegacyCredentialOption(args))
                {
                    await output.WriteErrorAsync(
                        "Credential values must be provided through --token-stdin or --api-key-stdin.").ConfigureAwait(false);
                    return CliExitCodes.Usage;
                }

                if (parseResult.Errors.Count > 0)
                {
                    var usageConfiguration = CreateInvocationConfiguration(stdout, stderr);
                    _ = await parseResult.InvokeAsync(usageConfiguration, cancellationToken).ConfigureAwait(false);
                    return CliExitCodes.Usage;
                }

                // The container was built from a raw-argument read of the flag. If the parser disagrees,
                // the already-constructed secure storage is running under a policy the user did not ask
                // for, so the invocation is refused rather than continued under the weaker of the two.
                if (!CliSecureStorageOption.MatchesParsedValue(
                        bootstrapDowngradePolicy,
                        parseResult.GetValue(commandFactory.AllowInsecureStorageOption)))
                {
                    await output.WriteErrorAsync(
                        $"Could not determine whether {CliSecureStorageOption.Name} was requested; "
                        + "pass it as its own argument before the command.").ConfigureAwait(false);
                    return CliExitCodes.Usage;
                }

                if (!IsMetadataOnlyInvocation(args))
                {
                    try
                    {
                        await services.GetRequiredService<IConfigurationRecoveryService>()
                            .RecoverPendingTransactionsAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (ConfigurationPersistenceException exception)
                    {
                        await output.WriteErrorAsync($"Startup recovery failed: {CliPersistenceFailure.Describe(exception)}").ConfigureAwait(false);
                        return CliExitCodes.Failure;
                    }
                }

                var configuration = CreateInvocationConfiguration(stdout, stderr);
                return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await fallbackWarningState.WriteIfNeededAsync(output).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteErrorAsync("CLI invocation was cancelled.").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }
        catch (StdioCommandLineParseException exception)
        {
            await output.WriteErrorAsync($"Invalid --stdio-args: {exception.Message}").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }
        catch (ConfigurationPersistenceException exception)
        {
            await output.WriteErrorAsync($"CLI failed: {CliPersistenceFailure.Describe(exception)}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }
        catch (Exception exception)
        {
            // Do not print a stack trace or exception message: future handlers may carry credential
            // material in an exception. The exception type is enough for a stable, actionable
            // process-level failure while details remain available to a host-owned logger later.
            await output.WriteErrorAsync($"CLI failed: {exception.GetType().Name}.").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }
    }

    private static bool IsMetadataOnlyInvocation(string[] args)
        => args.Length == 0 || args.Any(argument => argument is "--help" or "-h" or "--version");

    private static bool ContainsLegacyCredentialOption(string[] args)
        => args.Any(argument =>
            argument is "--token" or "--api-key" ||
            argument.StartsWith("--token=", StringComparison.Ordinal) ||
            argument.StartsWith("--api-key=", StringComparison.Ordinal));

    private static InvocationConfiguration CreateInvocationConfiguration(TextWriter stdout, TextWriter stderr)
    {
        return new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false
        };
    }
}

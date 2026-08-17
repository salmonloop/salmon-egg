using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
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
    internal static ServiceProvider CreateServiceProvider(ICliOutput output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));

        var services = new ServiceCollection();
        services.AddSingleton(output);
        services.AddSingleton(_ => new CliVersionProvider(typeof(CliApplication).Assembly));
        services.AddSingleton<CliCommandFactory>();
        services.AddSingleton<ServerConfigurationHandler>();
        services.AddSingleton<CredentialsHandler>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSalmonEggDesktopConfiguration();
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
        => RunAsyncCore(args, stdout, stderr, cancellationToken, null, null);

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
        => RunAsyncCore(args, stdout, stderr, cancellationToken, commandFactoryResolver, outputOverride);

    private static async Task<int> RunAsyncCore(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken,
        Func<IServiceProvider, CliCommandFactory>? commandFactoryResolver,
        ICliOutput? outputOverride)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (stdout is null) throw new ArgumentNullException(nameof(stdout));
        if (stderr is null) throw new ArgumentNullException(nameof(stderr));

        var output = outputOverride ?? new TextCliOutput(stdout, stderr);

        try
        {
            await using var services = CreateServiceProvider(output);
            var root = (commandFactoryResolver is null
                ? services.GetRequiredService<CliCommandFactory>()
                : commandFactoryResolver(services)).CreateRootCommand();
            var parseResult = root.Parse(args);

            if (args.Length == 0)
            {
                var helpParseResult = root.Parse(["--help"]);
                var helpConfiguration = CreateInvocationConfiguration(stdout, stderr);
                _ = await helpParseResult.InvokeAsync(helpConfiguration, cancellationToken).ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            if (parseResult.Errors.Count > 0)
            {
                var usageConfiguration = CreateInvocationConfiguration(stdout, stderr);
                _ = await parseResult.InvokeAsync(usageConfiguration, cancellationToken).ConfigureAwait(false);
                return CliExitCodes.Usage;
            }

            var configuration = CreateInvocationConfiguration(stdout, stderr);
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteErrorAsync("CLI invocation was cancelled.").ConfigureAwait(false);
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

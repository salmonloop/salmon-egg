using System;
using System.IO;
using System.Threading.Tasks;

namespace SalmonEgg.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_WithRootHelp_WritesCommandGroupsToStdout()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["--help"], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Salmon Egg configuration management CLI", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("config", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("credentials", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("set-credential", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("clear-credential", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("has-credential", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("config", "Manage configuration")]
    [InlineData("credentials", "Credential commands")]
    public async Task RunAsync_WithStructuralGroupHelp_WritesOnlyThatGroupHelp(string command, string description)
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync([command, "--help"], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains(description, stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("config", "Manage configuration")]
    [InlineData("credentials", "Credential commands")]
    public async Task RunAsync_WithStructuralGroup_WritesNamespaceGuidance(string command, string description)
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync([command], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains(description, stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains($"salmon-egg {command} --help", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }
    [Fact]
    public async Task RunAsync_WithNoArguments_WritesRootHelpAndSucceeds()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync([], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage:", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("config", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_WithVersion_WritesCliAssemblyVersion()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["--version"], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("1.0.5.0", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_SetCredential_RequiresExactlyOneCredentialOption()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["set-credential", "server-id", "--token", "token-value", "--api-key", "api-key-value"],
            stdout,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Specify exactly one of --token or --api-key.", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("api-key-value", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ConfigServerAdd_AcceptsCanonicalStreamableHttpTransport()
    {
        var appDataRoot = Path.Combine(
            Path.GetTempPath(),
            "SalmonEggCliTests",
            Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", appDataRoot, EnvironmentVariableTarget.Process);
        try
        {
            await using var stdout = new StringWriter();
            await using var stderr = new StringWriter();

            var exitCode = await CliApplication.RunAsync(
                [
                    "config", "server", "add",
                    "--name", "HTTP Agent",
                    "--url", "https://agent.example",
                    "--transport", "streamable_http"
                ],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Server added:", stdout.ToString(), StringComparison.Ordinal);
            Assert.Empty(stderr.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("config", "--unknown-option")]
    public async Task RunAsync_WithInvalidInput_ReturnsUsageAndWritesParserDiagnostic(params string[] args)
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(args, stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.NotEmpty(stderr.ToString());
    }
}

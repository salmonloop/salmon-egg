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
        // 期望值取自仓库根的共享版本，而不是写死字面量：CLI 与 GUI 共用同一个版本真相源，
        // 写死会让每次发布抬版本都变成一次红灯。
        var expectedVersionPrefix = RepositoryLayout.ReadSharedDisplayVersion() + ".";
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["--version"], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.StartsWith(expectedVersionPrefix, stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_SetCredential_RequiresExactlyOneCredentialOption()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["set-credential", "server-id", "--token-stdin", "--api-key-stdin"],
            stdout,
            stderr,
            new StringReader("credential-value\n"),
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Specify exactly one of --token-stdin or --api-key-stdin.", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("credential-value", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ConfigServerAdd_ParsesQuotedDashPrefixedStdioArguments()
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
                    "--name", "Stdio Agent",
                    "--transport", "stdio",
                    "--stdio-command", "agent",
                    "--stdio-args=--serve -T --mode plan"
                ],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            var id = stdout.ToString().Split(':', 2)[1].Trim();
            Assert.Empty(stderr.ToString());

            await using var showStdout = new StringWriter();
            await using var showStderr = new StringWriter();
            var showExitCode = await CliApplication.RunAsync(
                ["config", "server", "show", id],
                showStdout,
                showStderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, showExitCode);
            Assert.Contains("args:       --serve -T --mode plan", showStdout.ToString(), StringComparison.Ordinal);
            Assert.Empty(showStderr.ToString());

            await using var updateStdout = new StringWriter();
            await using var updateStderr = new StringWriter();
            var updateExitCode = await CliApplication.RunAsync(
                ["config", "server", "update", id, "--stdio-args="],
                updateStdout,
                updateStderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, updateExitCode);
            Assert.Empty(updateStderr.ToString());

            await using var clearedShowStdout = new StringWriter();
            await using var clearedShowStderr = new StringWriter();
            var clearedShowExitCode = await CliApplication.RunAsync(
                ["config", "server", "show", id],
                clearedShowStdout,
                clearedShowStderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, clearedShowExitCode);
            Assert.DoesNotContain("args:", clearedShowStdout.ToString(), StringComparison.Ordinal);
            Assert.Empty(clearedShowStderr.ToString());
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

    [Fact]
    public async Task RunAsync_ConfigServerAdd_AcceptsDetachedStdioArgumentsValue()
    {
        // The documented form is attached (--stdio-args="..."), but a detached value is the shape
        // users type by habit. Both must reach the child process as the same argv.
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
                    "--name", "Detached Stdio Agent",
                    "--transport", "stdio",
                    "--stdio-command", "agent",
                    "--stdio-args", "--serve -T --mode plan"
                ],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Empty(stderr.ToString());
            var id = stdout.ToString().Split(':', 2)[1].Trim();

            await using var showStdout = new StringWriter();
            await using var showStderr = new StringWriter();
            var showExitCode = await CliApplication.RunAsync(
                ["config", "server", "show", id],
                showStdout,
                showStderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, showExitCode);
            Assert.Contains("args:       --serve -T --mode plan", showStdout.ToString(), StringComparison.Ordinal);
            Assert.Empty(showStderr.ToString());
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
    [InlineData("--stdio-args")]
    [InlineData("--stdio-args=--serve \"unterminated")]
    public async Task RunAsync_ConfigServerAdd_WithInvalidStdioArguments_ReturnsUsageWithoutWriting(string stdioOption)
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "SalmonEggCliTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", appDataRoot, EnvironmentVariableTarget.Process);
        try
        {
            await using var stdout = new StringWriter();
            await using var stderr = new StringWriter();

            var exitCode = await CliApplication.RunAsync(
                [
                    "config", "server", "add",
                    "--name", "Invalid Args",
                    "--transport", "stdio",
                    "--stdio-command", "agent",
                    stdioOption
                ],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("Invalid --stdio-args:", stderr.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(appDataRoot, "config", "servers")));
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

    [Fact]
    public async Task RunAsync_ConfigServerAdd_WithTokenStdin_DoesNotExposeCredential()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "SalmonEggCliTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", appDataRoot, EnvironmentVariableTarget.Process);
        const string secret = "stdin-secret-value";
        try
        {
            await using var stdout = new StringWriter();
            await using var stderr = new StringWriter();
            using var stdin = new StringReader(secret + Environment.NewLine);

            // --allow-insecure-storage keeps this test about credential secrecy rather than about which
            // secret store the host machine happens to have: the write succeeds on a runner with a
            // keychain and on one without, so the "value never leaks" assertions below are what varies.
            var exitCode = await CliApplication.RunAsync(
                [
                    "--allow-insecure-storage",
                    "config", "server", "add",
                    "--name", "Secret Agent",
                    "--url", "https://agent.example",
                    "--token-stdin"
                ],
                stdout,
                stderr,
                stdin,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.DoesNotContain(secret, stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, stderr.ToString(), StringComparison.Ordinal);
            var yaml = Assert.Single(Directory.EnumerateFiles(Path.Combine(appDataRoot, "config", "servers"), "*.yaml"));
            Assert.DoesNotContain(secret, await File.ReadAllTextAsync(yaml, TestContext.Current.CancellationToken), StringComparison.Ordinal);
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

    [Fact]
    public async Task RunAsync_ConfigServerAdd_WithTokenStdinAtEndOfInput_ReturnsUsageWithoutWriting()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "SalmonEggCliTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", appDataRoot, EnvironmentVariableTarget.Process);
        try
        {
            await using var stdout = new StringWriter();
            await using var stderr = new StringWriter();
            using var stdin = new StringReader(string.Empty);

            var exitCode = await CliApplication.RunAsync(
                [
                    "config", "server", "add",
                    "--name", "Missing Secret Agent",
                    "--url", "https://agent.example",
                    "--token-stdin"
                ],
                stdout,
                stderr,
                stdin,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("Credential value cannot be empty.", stderr.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(appDataRoot, "config", "servers")));
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

    [Fact]
    public async Task RunAsync_WithLegacyCredentialOption_RejectsWithoutEchoingValue()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();
        const string secret = "legacy-secret-value";

        var exitCode = await CliApplication.RunAsync(
            ["set-credential", "server-id", "--token", secret],
            stdout,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--token-stdin", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, stderr.ToString(), StringComparison.Ordinal);
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

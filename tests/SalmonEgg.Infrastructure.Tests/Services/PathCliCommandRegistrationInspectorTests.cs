using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Desktop.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

/// <summary>
/// PATH resolution, which is the only thing that decides what the user's shell actually runs.
/// </summary>
/// <remarks>
/// The separator and file name come from the fake, so these cases behave identically on every host: what is
/// being tested is the resolution order and the classification, not this machine's conventions.
/// </remarks>
public sealed class PathCliCommandRegistrationInspectorTests
{
    private const string AppVersion = "1.4.3-alpha.0.47+abc123";

    [Fact]
    public async Task APlatformWithoutAProcessHostReportsUnsupported()
    {
        // WebAssembly has no PATH and cannot start anything. Its own state rather than "not registered",
        // which would send a user looking for an installer that could fix it.
        var inspector = Create(new FakeProbeEnvironment(), supportsInspection: false);

        var registration = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.Unsupported, registration.State);
        Assert.Null(registration.ResolvedPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnAbsentSearchPathReportsNotRegistered(string? searchPath)
    {
        var inspector = Create(new FakeProbeEnvironment { SearchPath = searchPath });

        var registration = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.NotRegistered, registration.State);
    }

    [Fact]
    public async Task ASearchPathWithoutTheCommandReportsNotRegisteredWithoutStartingAnything()
    {
        var environment = new FakeProbeEnvironment { SearchPath = "/usr/bin:/bin" };

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.NotRegistered, registration.State);
        Assert.Empty(environment.ProbedExecutables);
    }

    [Fact]
    public async Task TheFirstMatchOnTheSearchPathWins()
    {
        // Two installations, and only the earlier one is reachable by typing the name. Returning the other
        // would describe a command the user cannot invoke -- and this is the case worth reporting, since it
        // is how a stale install shadows a current one.
        var environment = new FakeProbeEnvironment
        {
            SearchPath = "/opt/other/bin:/usr/local/bin",
            Files = { "/opt/other/bin/salmon-egg", "/usr/local/bin/salmon-egg" },
            Versions = { ["/opt/other/bin/salmon-egg"] = "1.0.0", ["/usr/local/bin/salmon-egg"] = AppVersion },
        };

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/opt/other/bin/salmon-egg", registration.ResolvedPath);
        Assert.Equal(CliCommandRegistrationState.VersionMismatch, registration.State);
        Assert.Equal("1.0.0", registration.ReportedVersion);
    }

    [Fact]
    public async Task ALaterEntryIsFoundWhenEarlierOnesDoNotHaveIt()
    {
        var environment = new FakeProbeEnvironment
        {
            SearchPath = "/usr/bin:/usr/local/bin:/opt/bin",
            Files = { "/usr/local/bin/salmon-egg" },
            Versions = { ["/usr/local/bin/salmon-egg"] = AppVersion },
        };

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/usr/local/bin/salmon-egg", registration.ResolvedPath);
        Assert.Equal(CliCommandRegistrationState.Registered, registration.State);
    }

    [Fact]
    public async Task EmptyAndQuotedSearchPathEntriesAreSkippedRatherThanFatal()
    {
        // A real PATH carries empty segments and quoted entries; a shell ignores the former and unwraps the
        // latter. Aborting the walk on one would report "not registered" for a command that works.
        var environment = new FakeProbeEnvironment
        {
            SearchPath = "::\"/usr/local/bin\": :/opt/bin",
            Files = { "/usr/local/bin/salmon-egg" },
            Versions = { ["/usr/local/bin/salmon-egg"] = AppVersion },
        };

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.Registered, registration.State);
        Assert.Equal("/usr/local/bin/salmon-egg", registration.ResolvedPath);
    }

    [Fact]
    public async Task AnExecutableThatWillNotReportItsVersionIsUnreadable()
    {
        // Distinct from "not registered": the file is there, so an installer did its job. Distinct from a
        // mismatch: nothing is known about what it is. That is a broken install, not a stale one.
        var environment = new FakeProbeEnvironment
        {
            SearchPath = "/usr/local/bin",
            Files = { "/usr/local/bin/salmon-egg" },
            Failures = { ["/usr/local/bin/salmon-egg"] = "the command exited with code 134" },
        };

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.Unreadable, registration.State);
        Assert.Equal("/usr/local/bin/salmon-egg", registration.ResolvedPath);
        Assert.Equal("the command exited with code 134", registration.FailureDetail);
        Assert.Null(registration.ReportedVersion);
    }

    [Fact]
    public async Task ALinkTargetIsReportedOnlyWhenItDiffers()
    {
        var linked = new FakeProbeEnvironment
        {
            SearchPath = "/usr/bin",
            Files = { "/usr/bin/salmon-egg" },
            Versions = { ["/usr/bin/salmon-egg"] = AppVersion },
            LinkTargets = { ["/usr/bin/salmon-egg"] = "/opt/salmon-egg/cli/salmon-egg" },
        };
        var plain = new FakeProbeEnvironment
        {
            SearchPath = "/usr/bin",
            Files = { "/usr/bin/salmon-egg" },
            Versions = { ["/usr/bin/salmon-egg"] = AppVersion },
            LinkTargets = { ["/usr/bin/salmon-egg"] = "/usr/bin/salmon-egg" },
        };

        var viaLink = await Create(linked).InspectAsync(TestContext.Current.CancellationToken);
        var direct = await Create(plain).InspectAsync(TestContext.Current.CancellationToken);

        // Where the link leads is the actionable part: a link into an app bundle the user has since replaced
        // is how a stale command survives an upgrade.
        Assert.Equal("/opt/salmon-egg/cli/salmon-egg", viaLink.ResolvedTargetPath);
        Assert.Null(direct.ResolvedTargetPath);
    }

    [Fact]
    public async Task TheCommandFileNameAndSeparatorComeFromTheEnvironment()
    {
        // The Windows shape: a semicolon-separated PATH and a name carrying the extension, which is where an
        // MSIX alias lives. The candidate path is built through the environment's own grammar rather than
        // spelled out or joined with the host's Path.Combine, so this case asserts the file name and the
        // separator without inheriting the running machine's path conventions.
        var environment = new FakeProbeEnvironment
        {
            SearchPath = @"C:\Users\u\AppData\Local\Microsoft\WindowsApps",
            Separator = ';',
            FileName = "salmon-egg.exe",
        };
        var candidate = environment.Combine(environment.GetSearchPath()!, environment.CommandFileName);
        environment.Files.Add(candidate);
        environment.Versions[candidate] = AppVersion;

        var registration = await Create(environment).InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliCommandRegistrationState.Registered, registration.State);
        Assert.Equal(candidate, registration.ResolvedPath);
    }

    [Theory]
    // The CLI prints its informational version while the app knows its assembly version, so a matched pair
    // never compares equal verbatim. Only the release identity MinVer derives from the tag is compared.
    [InlineData("1.4.3-alpha.0.47+abc123", "1.4.3.0", CliCommandRegistrationState.Registered)]
    [InlineData("1.4.3", "1.4.3.0", CliCommandRegistrationState.Registered)]
    [InlineData("1.4.3.0", "1.4.3-alpha.0.47+abc", CliCommandRegistrationState.Registered)]
    [InlineData("1.4.4", "1.4.3.0", CliCommandRegistrationState.VersionMismatch)]
    [InlineData("1.5.0-alpha.1", "1.4.3.0", CliCommandRegistrationState.VersionMismatch)]
    [InlineData("2.0.0", "1.4.3.0", CliCommandRegistrationState.VersionMismatch)]
    public void VersionsAreComparedOnReleaseIdentityAlone(string reported, string expected, CliCommandRegistrationState state)
    {
        var registration = CliCommandRegistration.Resolved("/usr/local/bin/salmon-egg", null, reported, expected);

        Assert.Equal(state, registration.State);
        Assert.Equal(reported, registration.ReportedVersion);
        Assert.Equal(expected, registration.ExpectedVersion);
    }

    private static PathCliCommandRegistrationInspector Create(
        FakeProbeEnvironment environment,
        bool supportsInspection = true) =>
        new(environment, new FakeCapabilities(supportsInspection), AppVersion);

    private sealed class FakeProbeEnvironment : ICliCommandProbeEnvironment
    {
        public string? SearchPath { get; set; }

        public char Separator { get; set; } = ':';

        public string FileName { get; set; } = "salmon-egg";

        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Versions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Failures { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> LinkTargets { get; } = new(StringComparer.Ordinal);

        public List<string> ProbedExecutables { get; } = [];

        public string? GetSearchPath() => SearchPath;

        public char SearchPathSeparator => Separator;

        public string CommandFileName => FileName;

        // Deterministic Unix grammar: the inventory is spelled with forward slashes, and the candidates the
        // inspector asks about must land on exactly those strings on every host. The real machine supplies
        // Path.Combine (via SystemCliCommandProbeEnvironment) with the host's own grammar instead.
        public string Combine(string directory, string fileName) =>
            directory.Length == 0 ? fileName
            : directory.EndsWith('/') ? directory + fileName
            : directory + '/' + fileName;

        public bool FileExists(string path) => Files.Contains(path);

        public string? ResolveLinkTarget(string path) => LinkTargets.GetValueOrDefault(path);

        public Task<CliVersionProbe> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken)
        {
            ProbedExecutables.Add(executablePath);

            if (Failures.TryGetValue(executablePath, out var failure))
            {
                return Task.FromResult(CliVersionProbe.Failure(failure));
            }

            return Task.FromResult(Versions.TryGetValue(executablePath, out var version)
                ? CliVersionProbe.Success(version)
                : CliVersionProbe.Failure("no version configured for this path"));
        }
    }

    private sealed class FakeCapabilities(bool supportsInspection) : IPlatformCapabilityService
    {
        public bool SupportsLaunchOnStartup => false;

        public bool SupportsTray => false;

        public bool SupportsLanguageOverride => true;

        public bool SupportsMiniWindow => false;

        public bool SupportsExternalFileOpen => false;

        public bool SupportsLocalFileExport => false;

        public bool SupportsStdioTransport => false;

        public bool SupportsInteractiveTerminalSurface => false;

        public bool SupportsLocalTerminal => false;

        public bool SupportsGamepadInput => false;

        public bool SupportsCliCommandInspection { get; } = supportsInspection;

        public bool SupportsCliCommandLinking => false;
    }
}

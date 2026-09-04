using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Desktop.Services;

/// <summary>
/// Answers "would a shell reach this app's command" by resolving PATH the way a shell does.
/// </summary>
/// <remarks>
/// PATH resolution rather than looking where each installer puts things. The app cannot know which
/// installer delivered it — the MSIX and the desktop MSI produce the same running app from different
/// locations — and even if it could, checking the expected location would answer a different question.
/// What a user cares about is what happens when they type the name, and the only thing that decides that
/// is PATH order: an entry from another installation earlier on PATH wins, which is precisely the case
/// worth reporting.
/// </remarks>
public sealed class PathCliCommandRegistrationInspector : ICliCommandRegistrationInspector
{
    private readonly ICliCommandProbeEnvironment _environment;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly string _expectedVersion;

    public PathCliCommandRegistrationInspector(IPlatformCapabilityService capabilities)
        : this(new SystemCliCommandProbeEnvironment(), capabilities, ReadOwnVersion())
    {
    }

    public PathCliCommandRegistrationInspector(
        ICliCommandProbeEnvironment environment,
        IPlatformCapabilityService capabilities,
        string expectedVersion)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _expectedVersion = expectedVersion ?? throw new ArgumentNullException(nameof(expectedVersion));
    }

    public async Task<CliCommandRegistration> InspectAsync(CancellationToken cancellationToken = default)
    {
        // WebAssembly has neither a PATH nor a way to start a process, so there is no question to answer.
        // Reported as its own state rather than as "not registered": the latter invites a user to go
        // looking for an installer that could fix it.
        if (!_capabilities.SupportsCliCommandInspection)
        {
            return CliCommandRegistration.Unsupported(_expectedVersion);
        }

        var resolved = ResolveOnSearchPath();
        if (resolved is null)
        {
            return CliCommandRegistration.NotRegistered(_expectedVersion);
        }

        var target = _environment.ResolveLinkTarget(resolved);
        // Only reported when it says something the resolved path does not.
        var reportedTarget = string.Equals(target, resolved, StringComparison.Ordinal) ? null : target;

        var probe = await _environment.ProbeVersionAsync(resolved, cancellationToken).ConfigureAwait(false);
        if (probe.Version is null)
        {
            return CliCommandRegistration.Unreadable(
                resolved,
                reportedTarget,
                _expectedVersion,
                probe.FailureDetail ?? "the command did not report a version");
        }

        return CliCommandRegistration.Resolved(resolved, reportedTarget, probe.Version, _expectedVersion);
    }

    /// <summary>
    /// Walks PATH in order and returns the first entry that holds the command, as a shell would.
    /// </summary>
    /// <remarks>
    /// Order matters and is the whole point: with two installations on PATH, the first one wins, and
    /// returning any other match would describe a command the user cannot actually invoke. Unparseable
    /// entries are skipped rather than aborting the walk — PATH routinely carries empty segments and
    /// directories that no longer exist, and a shell ignores those too.
    /// </remarks>
    private string? ResolveOnSearchPath()
    {
        var searchPath = _environment.GetSearchPath();
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        var fileName = _environment.CommandFileName;
        foreach (var entry in searchPath.Split(_environment.SearchPathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = _environment.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // An entry with characters the platform rejects in a path. A shell cannot use it either.
                continue;
            }

            if (_environment.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The running app's own version, which is what a matched command must report.
    /// </summary>
    /// <remarks>
    /// The informational version is preferred because it is what the CLI prints, so a matched pair compares
    /// like against like; the assembly version is the fallback because MinVer always sets it, while the
    /// informational attribute can be stripped. Only the release identity is ever compared (see
    /// CliCommandRegistration), so the difference in suffix between the two does not matter.
    /// </remarks>
    private static string ReadOwnVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString() ?? string.Empty
            : informational;
    }
}

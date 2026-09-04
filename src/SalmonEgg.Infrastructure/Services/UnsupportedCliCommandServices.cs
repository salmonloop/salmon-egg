using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

/// <summary>
/// The answer on platforms with neither a PATH nor a process host.
/// </summary>
/// <remarks>
/// WebAssembly, Android and iOS have no shell command to reach, so they get an implementation that says so
/// rather than the view model learning which platform it is on. Keeping the capability question inside the
/// service is what lets one settings page serve every target.
/// </remarks>
public sealed class UnsupportedCliCommandRegistrationInspector : ICliCommandRegistrationInspector
{
    public Task<CliCommandRegistration> InspectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CliCommandRegistration.Unsupported(ReadOwnVersion()));

    private static string ReadOwnVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UnsupportedCliCommandRegistrationInspector).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? string.Empty;
    }
}

/// <summary>
/// Refuses to own the command's PATH entry, which is the correct answer everywhere but macOS.
/// </summary>
/// <remarks>
/// Registered on the platforms whose installer owns the entry as well as on those with no entry at all. Both
/// cases are "not the app's to change": on Windows and Linux a second owner would fight the installer, and
/// the loser of that race leaves a command pointing at a deleted app.
/// </remarks>
public sealed class UnsupportedCliCommandLinkService : ICliCommandLinkService
{
    public bool IsSupported => false;

    public Task<CliCommandLinkResult> LinkAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CliCommandLinkResult.Unsupported());

    public Task<CliCommandLinkResult> UnlinkAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CliCommandLinkResult.Unsupported());
}

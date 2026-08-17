using System;
using System.Reflection;

namespace SalmonEgg.Cli.Hosting;

/// <summary>
/// Resolves the CLI executable version without coupling the console host to the GUI assembly.
/// </summary>
public sealed class CliVersionProvider
{
    private readonly Assembly _assembly;

    public CliVersionProvider(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    /// <summary>
    /// Gets the most descriptive version metadata available for the CLI executable.
    /// </summary>
    public string Version => ResolveVersion(_assembly);

    internal static string ResolveVersion(Assembly assembly)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

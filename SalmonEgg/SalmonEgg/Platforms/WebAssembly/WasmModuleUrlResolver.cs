#if __WASM__
using System;

namespace SalmonEgg.Platforms.WebAssembly;

/// <summary>
/// Resolves the served URL of a bundled WasmScripts ES module.
/// </summary>
/// <remarks>
/// The bootstrapper publishes these modules under the app's <c>_framework</c> directory, and the app
/// may be hosted under a sub-path. Uno exposes both segments as environment variables, so this is the
/// single place that assembles the URL rather than each interop service repeating it.
/// </remarks>
internal static class WasmModuleUrlResolver
{
    public static string Resolve(string moduleName)
    {
        var appBase = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE"));
        if (string.IsNullOrWhiteSpace(appBase))
        {
            return "./" + moduleName;
        }

        var webAppBasePath = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH"));
        return string.IsNullOrWhiteSpace(webAppBasePath)
            ? $"/{appBase}/_framework/{moduleName}"
            : $"/{webAppBasePath}/{appBase}/_framework/{moduleName}";
    }

    private static string NormalizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('/');
}
#endif

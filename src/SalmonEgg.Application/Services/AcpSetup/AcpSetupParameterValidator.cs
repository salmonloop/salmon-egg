using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Application.Services.AcpSetup;

/// <summary>
/// One rejected parameter, carrying the localization key the presentation layer renders.
/// </summary>
public readonly record struct AcpSetupParameterViolation(string ParameterKey, string MessageKey);

/// <summary>
/// Validates wizard parameter values before a launch plan is tested or saved. Deliberately limited to
/// checks established by the launch contract: required values, closed sets, and unsupported runtime
/// entry points. Other agent-specific behavior is left to the connectivity test.
/// </summary>
public static class AcpSetupParameterValidator
{
    /// <summary>
    /// Localization key reported when a required parameter has no value. Public because the
    /// presentation layer resolves <see cref="AcpSetupParameterViolation.MessageKey"/> against its
    /// own resource table, so the value domain is part of this type's published contract.
    /// </summary>
    public const string MissingRequiredValueKey = "AcpSetup_Validation_MissingRequiredValue";

    /// <summary>
    /// Localization key reported when a value falls outside the parameter's declared closed set.
    /// </summary>
    public const string ValueNotAllowedKey = "AcpSetup_Validation_ValueNotAllowed";

    public const string RuntimeBatchLauncherNotSupportedKey = "AcpSetup_Validation_RuntimeBatchLauncherNotSupported";

    public static IReadOnlyList<AcpSetupParameterViolation> Validate(AcpSetupDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var violations = new List<AcpSetupParameterViolation>(Validate(draft.Adapter.LaunchTemplate, draft.ParameterValues));
        if (!draft.Adapter.SupportsWindowsRuntimeBatchLauncher
            && !string.IsNullOrWhiteSpace(draft.Adapter.RuntimeCommandEnvironmentVariable)
            && draft.CommandOverrides.TryGetOverride(draft.Agent.Runtime.ProbeCommand, out var path)
            && IsWindowsBatchLauncher(path))
        {
            violations.Add(new AcpSetupParameterViolation(draft.Agent.Runtime.ProbeCommand, RuntimeBatchLauncherNotSupportedKey));
        }

        return violations;
    }

    public static IReadOnlyList<AcpSetupParameterViolation> Validate(
        AcpLaunchTemplate template,
        IReadOnlyDictionary<string, string>? parameterValues)
    {
        ArgumentNullException.ThrowIfNull(template);

        var violations = new List<AcpSetupParameterViolation>();
        foreach (var parameter in template.Parameters)
        {
            var value = ResolveValue(parameterValues, parameter.Key);

            if (value.Length == 0)
            {
                if (parameter.IsRequired)
                {
                    violations.Add(new AcpSetupParameterViolation(parameter.Key, MissingRequiredValueKey));
                }

                continue;
            }

            if (parameter.AllowedValues.Count > 0
                && !ContainsValue(parameter.AllowedValues, value))
            {
                violations.Add(new AcpSetupParameterViolation(parameter.Key, ValueNotAllowedKey));
            }
        }

        return violations;
    }

    private static string ResolveValue(IReadOnlyDictionary<string, string>? values, string key)
        => values is not null && values.TryGetValue(key, out var value)
            ? (value ?? string.Empty).Trim()
            : string.Empty;

    private static bool ContainsValue(IReadOnlyList<string> allowedValues, string value)
    {
        foreach (var allowed in allowedValues)
        {
            if (string.Equals(allowed, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowsBatchLauncher(string path)
    {
        // Detect Windows drive, backslash and UNC syntax without consulting the host platform.
        // POSIX permits .cmd executable names and multiple leading slashes; those remain probeable.
        var uncShareSeparator = path.StartsWith("//", StringComparison.Ordinal)
            ? path.IndexOf('/', 2)
            : -1;
        var windowsPath = (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
                && path[2] is '\\' or '/')
            || path.Contains('\\')
            || (uncShareSeparator > 2 && uncShareSeparator + 1 < path.Length
                && path[uncShareSeparator + 1] != '/');
        return windowsPath && (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
    }
}

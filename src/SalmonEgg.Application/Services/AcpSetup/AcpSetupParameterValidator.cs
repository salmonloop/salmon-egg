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
/// checks that are certainly wrong regardless of agent: missing required values and values outside a
/// declared closed set. Anything agent-specific is left to the connectivity test, which reports real
/// failures instead of guesses.
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
}

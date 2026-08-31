using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Application.Services.AcpSetup;

/// <summary>
/// Turns a launch template plus the user's parameter values into a concrete launch plan.
/// Pure and deterministic: the same inputs always produce the same command line.
/// </summary>
public static class AcpLaunchPlanBuilder
{
    /// <summary>
    /// Seeds one value per template parameter, preferring the declared default. Callers use this as the
    /// prefilled form state so required parameters are visible even when they have no default.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CreatePrefilledValues(AcpLaunchTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in template.Parameters)
        {
            values[parameter.Key] = parameter.DefaultValue;
        }

        return values;
    }

    /// <summary>
    /// Builds the launch plan. Parameters with no value are omitted entirely rather than emitted as
    /// empty flags or empty environment variables, which agents reject.
    /// </summary>
    /// <param name="overrides">
    /// User-supplied paths for commands the catalog names by executable name. The same set is applied
    /// during detection, so what the wizard verified is what the saved profile starts.
    /// </param>
    public static AcpLaunchPlan Build(
        AcpLaunchTemplate template,
        IReadOnlyDictionary<string, string>? parameterValues,
        AcpCommandOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        var arguments = new List<string>(template.FixedArguments);
        var environment = new Dictionary<string, string>(template.FixedEnvironment, StringComparer.Ordinal);

        foreach (var parameter in template.Parameters)
        {
            if (parameter.IsSecret)
            {
                // Launch plans are persisted in clear text. Refuse rather than write a credential to disk;
                // see AcpSetupParameterDefinition.IsSecret.
                throw new InvalidOperationException(
                    $"Launch parameter '{parameter.Key}' is marked secret and cannot be carried in a launch plan.");
            }

            if (parameterValues is null
                || !parameterValues.TryGetValue(parameter.Key, out var rawValue))
            {
                continue;
            }

            var value = (rawValue ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (parameter.Target == AcpSetupParameterTarget.EnvironmentVariable)
            {
                environment[parameter.Key] = value;
            }
            else
            {
                arguments.Add(parameter.Key);
                arguments.Add(value);
            }
        }

        return new AcpLaunchPlan
        {
            Command = (overrides ?? AcpCommandOverrides.Empty).Resolve(template.Command),
            Arguments = arguments,
            Environment = environment
        };
    }
}

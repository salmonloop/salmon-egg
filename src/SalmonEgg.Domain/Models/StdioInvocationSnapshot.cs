using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SalmonEgg.Domain.Models;

/// <summary>A private-to-process copy of the invocation used by the current ACP connection.</summary>
/// <remarks>
/// Environment values may contain credentials. This is intentionally a class with no value-rendering
/// ToString, and must never be persisted, logged or displayed as a command preview.
/// </remarks>
public sealed class StdioInvocationSnapshot
{
    public StdioInvocationSnapshot(
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        bool environmentNamesIgnoreCase)
    {
        Command = command;
        Arguments = new ReadOnlyCollection<string>(new List<string>(arguments));
        Environment = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
            environment, environmentNamesIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
        WorkingDirectory = workingDirectory;
        EnvironmentNamesIgnoreCase = environmentNamesIgnoreCase;
    }

    public string Command { get; }

    public IReadOnlyList<string> Arguments { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public string WorkingDirectory { get; }

    public bool EnvironmentNamesIgnoreCase { get; }

    /// <summary>Append the method's arguments and apply its environment overrides as ACP requires.</summary>
    public StdioInvocationSnapshot WithAuthenticationMethod(
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        var combinedArguments = new List<string>(Arguments);
        if (arguments is not null)
        {
            combinedArguments.AddRange(arguments);
        }

        var combinedEnvironment = new Dictionary<string, string>(Environment,
            EnvironmentNamesIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                combinedEnvironment[entry.Key] = entry.Value;
            }
        }

        return new StdioInvocationSnapshot(Command, combinedArguments, combinedEnvironment,
            WorkingDirectory, EnvironmentNamesIgnoreCase);
    }
}

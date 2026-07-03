using System;
using System.Collections.Generic;
using System.Text;

namespace SalmonEgg.Domain.Models;

public static class StdioCommandLine
{
    public static IReadOnlyList<string> ParseArgumentsText(string? argumentsText)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var current = new StringBuilder();
        char? activeQuote = null;

        for (var index = 0; index < argumentsText.Length; index++)
        {
            var character = argumentsText[index];
            if (activeQuote == '"' && character == '\\' && index + 1 < argumentsText.Length)
            {
                var next = argumentsText[index + 1];
                if (next is '"' or '\\')
                {
                    current.Append(next);
                    index++;
                    continue;
                }
            }

            if (character is '"' or '\'')
            {
                if (activeQuote == character)
                {
                    activeQuote = null;
                    continue;
                }

                if (activeQuote == null)
                {
                    activeQuote = character;
                    continue;
                }
            }

            if (char.IsWhiteSpace(character) && activeQuote == null)
            {
                if (current.Length == 0)
                {
                    continue;
                }

                results.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            results.Add(current.ToString());
        }

        return results;
    }

    public static string FormatArgumentsText(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteIfNeeded(argument));
        }

        return builder.ToString();
    }

    public static string CanonicalizeArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            var value = argument ?? string.Empty;
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        return builder.ToString();
    }

    private static string QuoteIfNeeded(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var requiresQuoting = false;
        foreach (var character in argument)
        {
            if (char.IsWhiteSpace(character) || character == '"' || character == '\'')
            {
                requiresQuoting = true;
                break;
            }
        }

        if (!requiresQuoting)
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

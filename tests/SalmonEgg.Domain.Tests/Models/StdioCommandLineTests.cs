using NUnit.Framework;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class StdioCommandLineTests
{
    [Test]
    public void ParseArgumentsText_WithQuotedPath_ReturnsStructuredArguments()
    {
        var result = StdioCommandLine.ParseArgumentsText("-NoLogo -File \"/tmp/agent script.ps1\" --mode plan");

        Assert.That(result, Is.EqualTo(new[] { "-NoLogo", "-File", "/tmp/agent script.ps1", "--mode", "plan" }));
    }

    [Test]
    public void FormatArgumentsText_WithWhitespaceAndQuotes_RoundTrips()
    {
        string[] arguments = ["-File", "/tmp/agent script.ps1", "--name", "a\"b"];

        var formatted = StdioCommandLine.FormatArgumentsText(arguments);
        var reparsed = StdioCommandLine.ParseArgumentsText(formatted);

        Assert.That(reparsed, Is.EqualTo(arguments));
    }

    [Test]
    public void CanonicalizeArguments_WithDelimiterCharacters_IsUnambiguous()
    {
        string[] first = ["a|b", "c"];
        string[] second = ["a", "b|c"];

        var firstCanonical = StdioCommandLine.CanonicalizeArguments(first);
        var secondCanonical = StdioCommandLine.CanonicalizeArguments(second);

        Assert.That(firstCanonical, Is.Not.EqualTo(secondCanonical));
    }
}

using Xunit;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class StdioCommandLineTests
{
    [Fact]
    public void ParseArgumentsText_WithQuotedPath_ReturnsStructuredArguments()
    {
        var result = StdioCommandLine.ParseArgumentsText("-NoLogo -File \"/tmp/agent script.ps1\" --mode plan");

        Assert.Equal(new[] { "-NoLogo", "-File", "/tmp/agent script.ps1", "--mode", "plan" }, result);
    }

    [Fact]
    public void FormatArgumentsText_WithWhitespaceAndQuotes_RoundTrips()
    {
        string[] arguments = ["-File", "/tmp/agent script.ps1", "--name", "a\"b"];

        var formatted = StdioCommandLine.FormatArgumentsText(arguments);
        var reparsed = StdioCommandLine.ParseArgumentsText(formatted);

        Assert.Equal(arguments, reparsed);
    }

    [Fact]
    public void CanonicalizeArguments_WithDelimiterCharacters_IsUnambiguous()
    {
        string[] first = ["a|b", "c"];
        string[] second = ["a", "b|c"];

        var firstCanonical = StdioCommandLine.CanonicalizeArguments(first);
        var secondCanonical = StdioCommandLine.CanonicalizeArguments(second);

        Assert.NotEqual(secondCanonical, firstCanonical);
    }

    [Fact]
    public void ParseArgumentsText_WithEmptyQuotedArgument_PreservesEmptyArgument()
    {
        var result = StdioCommandLine.ParseArgumentsText("--flag \"\" value");

        Assert.Equal(new[] { "--flag", string.Empty, "value" }, result);
    }

    [Theory]
    [InlineData("--flag \"unterminated")]
    [InlineData("--flag 'unterminated")]
    public void ParseArgumentsText_WithUnterminatedQuote_ThrowsParseException(string arguments)
    {
        Assert.Throws<StdioCommandLineParseException>(() => StdioCommandLine.ParseArgumentsText(arguments));
    }
}

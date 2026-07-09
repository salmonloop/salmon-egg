using SalmonEgg.Presentation.Core.Services.Chat.Slash;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Slash;

public sealed class SlashInputParserTests
{
    [Fact]
    public void Parse_WhenInputIsPlainText_ReturnsNotSlashTextAndPreservesRawText()
    {
        const string input = "hello world";

        var result = SlashInputParser.Parse(input);

        Assert.Equal(SlashParseKind.NotSlashText, result.Kind);
        Assert.Equal(input, result.RawText);
        Assert.Equal(input, result.TrimmedStartText);
        Assert.Equal(0, result.LeadingWhitespaceCount);
        Assert.Empty(result.Tokens);
        Assert.Null(result.CommandToken);
        Assert.Empty(result.ArgumentTokens);
        Assert.Equal(0, result.CurrentTokenIndex);
        Assert.Equal(string.Empty, result.CurrentTokenText);
        Assert.False(result.HasTrailingSpace);
    }

    [Fact]
    public void Parse_WhenInputIsNull_TreatsItAsEmptyPlainText()
    {
        var result = SlashInputParser.Parse(null);

        Assert.Equal(SlashParseKind.NotSlashText, result.Kind);
        Assert.Equal(string.Empty, result.RawText);
        Assert.Equal(string.Empty, result.TrimmedStartText);
        Assert.Empty(result.Tokens);
        Assert.Null(result.CommandToken);
    }

    [Fact]
    public void Parse_WhenSlashCommandNameIsBeingEdited_PreservesLeadingWhitespaceAndCommandToken()
    {
        const string input = "  /pl";

        var result = SlashInputParser.Parse(input);

        Assert.Equal(SlashParseKind.EditingCommandName, result.Kind);
        Assert.Equal(input, result.RawText);
        Assert.Equal("/pl", result.TrimmedStartText);
        Assert.Equal(2, result.LeadingWhitespaceCount);
        Assert.Equal(new[] { "pl" }, result.Tokens.ToArray());
        Assert.Equal("pl", result.CommandToken);
        Assert.Empty(result.ArgumentTokens);
        Assert.Equal(0, result.CurrentTokenIndex);
        Assert.Equal("pl", result.CurrentTokenText);
        Assert.False(result.HasTrailingSpace);
    }

    [Fact]
    public void Parse_WhenOnlySlashIsPresent_StaysInCommandNameEditingState()
    {
        var result = SlashInputParser.Parse("/");

        Assert.Equal(SlashParseKind.EditingCommandName, result.Kind);
        Assert.Equal("/", result.TrimmedStartText);
        Assert.Empty(result.Tokens);
        Assert.Equal(string.Empty, result.CommandToken);
        Assert.Equal(0, result.CurrentTokenIndex);
        Assert.Equal(string.Empty, result.CurrentTokenText);
        Assert.False(result.HasTrailingSpace);
    }

    [Fact]
    public void Parse_WhenCommandEndsWithSpace_TransitionsToEditingArgumentToken()
    {
        const string input = "/plan ";

        var result = SlashInputParser.Parse(input);

        Assert.Equal(SlashParseKind.EditingArgumentToken, result.Kind);
        Assert.Equal(input, result.RawText);
        Assert.Equal("/plan ", result.TrimmedStartText);
        Assert.Equal(new[] { "plan" }, result.Tokens.ToArray());
        Assert.Equal("plan", result.CommandToken);
        Assert.Empty(result.ArgumentTokens);
        Assert.Equal(1, result.CurrentTokenIndex);
        Assert.Equal(string.Empty, result.CurrentTokenText);
        Assert.True(result.HasTrailingSpace);
    }

    [Fact]
    public void Parse_WhenArgumentsAreSeparatedByNonSpaceWhitespace_TokenizesThemAsArguments()
    {
        const string input = "/plan\tgoal";

        var result = SlashInputParser.Parse(input);

        Assert.Equal(SlashParseKind.EditingArgumentToken, result.Kind);
        Assert.Equal(new[] { "plan", "goal" }, result.Tokens.ToArray());
        Assert.Equal("plan", result.CommandToken);
        Assert.Equal(new[] { "goal" }, result.ArgumentTokens.ToArray());
        Assert.Equal(1, result.CurrentTokenIndex);
        Assert.Equal("goal", result.CurrentTokenText);
        Assert.False(result.HasTrailingSpace);
    }

    [Fact]
    public void Parse_WhenSlashPrefixHasUnmatchedCommandText_KeepsSlashEditingState()
    {
        const string input = "/plaaan";

        var result = SlashInputParser.Parse(input);

        Assert.Equal(SlashParseKind.EditingCommandName, result.Kind);
        Assert.Equal(new[] { "plaaan" }, result.Tokens.ToArray());
        Assert.Equal("plaaan", result.CommandToken);
        Assert.Empty(result.ArgumentTokens);
        Assert.Equal(0, result.CurrentTokenIndex);
        Assert.Equal("plaaan", result.CurrentTokenText);
        Assert.False(result.HasTrailingSpace);
    }
}

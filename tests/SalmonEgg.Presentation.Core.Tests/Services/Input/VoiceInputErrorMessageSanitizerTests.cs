using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Input;

public sealed class VoiceInputErrorMessageSanitizerTests
{
    [Fact]
    public void Normalize_WhenNull_ReturnsFallback()
    {
        Assert.Equal("fallback", VoiceInputErrorMessageSanitizer.Normalize(null, "fallback"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Normalize_WhenBlank_ReturnsFallback(string message)
    {
        Assert.Equal("fallback", VoiceInputErrorMessageSanitizer.Normalize(message, "fallback"));
    }

    [Theory]
    [InlineData("没有与此错误关联的文本")]
    [InlineData("No text is associated with this error")]
    [InlineData("NO TEXT IS ASSOCIATED WITH THIS ERROR")]
    public void Normalize_WhenSystemPlaceholder_ReturnsFallback(string message)
    {
        Assert.Equal("fallback", VoiceInputErrorMessageSanitizer.Normalize(message, "fallback"));
    }

    [Fact]
    public void Normalize_WhenValidMessage_ReturnsTrimmedMessage()
    {
        Assert.Equal("real error", VoiceInputErrorMessageSanitizer.Normalize("  real error  ", "fallback"));
    }

    [Fact]
    public void Normalize_WhenMessageIsPlaceholderButFallbackBlank_ReturnsBlankFallback()
    {
        // The placeholder must collapse to the fallback even when the fallback is empty,
        // so callers never surface the OS placeholder string to the user.
        Assert.Equal(string.Empty, VoiceInputErrorMessageSanitizer.Normalize("没有与此错误关联的文本", string.Empty));
    }

    [Fact]
    public void Normalize_DetectsPlaceholderPhraseEmbeddedInLongerMessage()
    {
        // Detection is a substring match, so the OS placeholder phrase embedded in a longer
        // message is still suppressed in favor of the fallback.
        var message = "Encountered: No text is associated with this error, then recovered";
        Assert.Equal("fallback", VoiceInputErrorMessageSanitizer.Normalize(message, "fallback"));
    }
}

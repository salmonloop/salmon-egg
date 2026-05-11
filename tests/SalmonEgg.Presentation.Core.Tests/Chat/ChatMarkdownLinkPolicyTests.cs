using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatMarkdownLinkPolicyTests
{
    [Theory]
    [InlineData("https://example.com/docs")]
    [InlineData("http://example.com/docs")]
    public void TryResolveLaunchUri_AllowsHttpAndHttps(string rawLink)
    {
        var resolved = ChatMarkdownLinkPolicy.TryResolveLaunchUri(rawLink, out var uri);

        Assert.True(resolved);
        Assert.NotNull(uri);
        Assert.Equal(rawLink, uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy-microphone")]
    [InlineData("mailto:test@example.com")]
    [InlineData("salmonegg://chat/session")]
    [InlineData("/relative/path")]
    [InlineData("not a uri")]
    public void TryResolveLaunchUri_RejectsUnsafeOrNonAbsoluteSchemes(string rawLink)
    {
        var resolved = ChatMarkdownLinkPolicy.TryResolveLaunchUri(rawLink, out var uri);

        Assert.False(resolved);
        Assert.Null(uri);
    }

    [Fact]
    public void TryResolveLaunchUri_FromUri_ReusesSamePolicy()
    {
        var httpsUri = new Uri("https://example.com/docs");
        var fileUri = new Uri("file:///tmp/example.txt");

        var allowsHttps = ChatMarkdownLinkPolicy.TryResolveLaunchUri(httpsUri, out var resolvedHttpsUri);
        var allowsFile = ChatMarkdownLinkPolicy.TryResolveLaunchUri(fileUri, out var resolvedFileUri);

        Assert.True(allowsHttps);
        Assert.Same(httpsUri, resolvedHttpsUri);
        Assert.False(allowsFile);
        Assert.Null(resolvedFileUri);
    }
}

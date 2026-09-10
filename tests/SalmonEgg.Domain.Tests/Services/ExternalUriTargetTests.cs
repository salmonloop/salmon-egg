using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Domain.Tests.Services;

public sealed class ExternalUriTargetTests
{
    [Theory]
    [InlineData("https://example.com/authorize?canary=private#step", "example.com", false)]
    [InlineData("https://xn--bcher-kva.example/path", "xn--bcher-kva.example", true)]
    [InlineData("https://bücher.example/path", "xn--bcher-kva.example", true)]
    [InlineData("http://127.0.0.1:1234/authorize", "127.0.0.1", true)]
    [InlineData("http://[::1]:1234/authorize", "::1", true)]
    [InlineData("http://localhost/authorize", "localhost", false)]
    public void TryCreate_AllowedTarget_PreservesFullUrlAndHighlightsHost(string url, string host, bool warning)
    {
        // Act
        var success = ExternalUriTarget.TryCreate(url, out var target);

        // Assert
        Assert.True(success);
        Assert.NotNull(target);
        Assert.Equal(url, target.FullUrl);
        Assert.Equal(host, target.Host);
        Assert.Equal(warning, target.HasAmbiguousHost);
        Assert.DoesNotContain(url, target.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,secret")]
    [InlineData("http://example.com/path")]
    [InlineData("https://trusted.example@evil.example/authorize")]
    [InlineData("https://example.com\\@evil.example/")]
    [InlineData("https://example.com/\u202Esecret")]
    [InlineData("https://example.com/\nsecret")]
    [InlineData(" https://example.com/")]
    [InlineData("http://127.0.0.2/authorize")]
    [InlineData("http://localhost./authorize")]
    [InlineData("")]
    [InlineData(null)]
    public void TryCreate_UnsafeTarget_DoesNotCreateNavigationIntent(string? url)
    {
        // Act
        var success = ExternalUriTarget.TryCreate(url, out var target);

        // Assert
        Assert.False(success);
        Assert.Null(target);
    }
}

using System;
using System.IO;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Tests;

public sealed class NavigationCoreTests
{
    [Fact]
    public void NavTimeFormatter_ToRelativeText_UsesExpectedBuckets()
    {
        var now = DateTime.UtcNow;

        Assert.Equal("刚刚", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30)));
        Assert.Equal("2 分", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2)));
        Assert.Equal("3 小时", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3)));
        Assert.Equal("2 天", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2)));
    }

    [Fact]
    public void NavTimeFormatter_NormalizePathForPrefixMatch_AppendsSeparator()
    {
        var path = Path.Combine("C:", "Temp", "Demo");
        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(path);

        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), normalized, StringComparison.Ordinal);
        Assert.Contains("Demo", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavItemTag_MoreTag_RoundTrips()
    {
        var tag = NavItemTag.More("proj-9");

        Assert.True(NavItemTag.TryParseMore(tag, out var projectId));
        Assert.Equal("proj-9", projectId);
    }

    [Fact]
    public void NavItemTag_SessionTag_RoundTrips()
    {
        var tag = NavItemTag.Session("session-42");

        Assert.True(NavItemTag.TryParseSession(tag, out var sessionId));
        Assert.Equal("session-42", sessionId);
    }

    [Fact]
    public void NavItemTag_ParseRejectsInvalid()
    {
        Assert.False(NavItemTag.TryParseSession("Session:", out _));
        Assert.False(NavItemTag.TryParseSession("Other:123", out _));
        Assert.False(NavItemTag.TryParseMore("More:", out _));
        Assert.False(NavItemTag.TryParseMore("Other:123", out _));
    }
}

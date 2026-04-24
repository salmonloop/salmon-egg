using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class LocalTerminalCwdResolverTests
{
    [Fact]
    public void Resolve_LocalSessionWithSessionInfoCwd_ReturnsSessionInfoCwd()
    {
        // Arrange
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");

        // Act
        var result = resolver.Resolve(isLocalSession: true, sessionInfoCwd: @"C:\repo\demo");

        // Assert
        Assert.Equal(@"C:\repo\demo", result);
    }

    [Fact]
    public void Resolve_LocalSessionWithSurroundingWhitespaceInSessionInfoCwd_ReturnsTrimmedPath()
    {
        // Arrange
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");

        // Act
        var result = resolver.Resolve(isLocalSession: true, sessionInfoCwd: @"  C:\repo\demo  ");

        // Assert
        Assert.Equal(@"C:\repo\demo", result);
    }

    [Fact]
    public void Resolve_NonLocalSession_FallsBackToUserHome()
    {
        // Arrange
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");

        // Act
        var result = resolver.Resolve(isLocalSession: false, sessionInfoCwd: @"C:\repo\demo");

        // Assert
        Assert.Equal(@"C:\Users\shang", result);
    }

    [Fact]
    public void Resolve_LocalSessionWithNullOrWhitespaceCwd_FallsBackToUserHome()
    {
        // Arrange
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");

        // Act
        var nullResult = resolver.Resolve(isLocalSession: true, sessionInfoCwd: null);
        var whitespaceResult = resolver.Resolve(isLocalSession: true, sessionInfoCwd: "  ");

        // Assert
        Assert.Equal(@"C:\Users\shang", nullResult);
        Assert.Equal(@"C:\Users\shang", whitespaceResult);
    }
}

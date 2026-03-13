using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class SessionCwdResolverTests
{
    [Fact]
    public void Resolve_Prioritizes_PendingProjectRoot()
    {
        var result = SessionCwdResolver.Resolve("C:\\Repo\\ProjectA", "C:\\Repo\\ProjectB");

        Assert.Equal("C:\\Repo\\ProjectA", result);
    }

    [Fact]
    public void Resolve_FallsBack_To_LastSelectedProjectRoot()
    {
        var result = SessionCwdResolver.Resolve(null, "C:\\Repo\\ProjectB");

        Assert.Equal("C:\\Repo\\ProjectB", result);
    }

    [Fact]
    public void Resolve_ReturnsNull_When_NoRootsProvided()
    {
        var result = SessionCwdResolver.Resolve("  ", null);

        Assert.Null(result);
    }
}

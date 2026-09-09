using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class StdioInvocationSnapshotTests
{
    [Fact]
    public void Constructor_MutableSource_IsolatesInvocationAndDoesNotRenderSecrets()
    {
        // Arrange
        var arguments = new List<string> { "--token=secret-argument" };
        var environment = new Dictionary<string, string> { ["TOKEN"] = "secret-environment" };

        // Act
        var snapshot = new StdioInvocationSnapshot("agent", arguments, environment, "/work", false);
        arguments.Clear();
        environment["TOKEN"] = "changed";

        // Assert
        Assert.Equal("--token=secret-argument", Assert.Single(snapshot.Arguments));
        Assert.Equal("secret-environment", snapshot.Environment["TOKEN"]);
        Assert.DoesNotContain("secret", snapshot.ToString());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void WithAuthenticationMethod_AppendsArgumentsAndUsesPlatformEnvironmentComparer(bool ignoreCase, int count)
    {
        // Arrange
        var snapshot = new StdioInvocationSnapshot("agent", ["--acp"],
            new Dictionary<string, string> { ["TOKEN"] = "base" }, "/work", ignoreCase);

        // Act
        var login = snapshot.WithAuthenticationMethod(["login", "--interactive"],
            new Dictionary<string, string> { ["token"] = "override" });

        // Assert
        Assert.Equal(new[] { "--acp", "login", "--interactive" }, login.Arguments);
        Assert.Equal(count, login.Environment.Count);
        Assert.Equal("override", login.Environment["token"]);
        Assert.Equal("base", snapshot.Environment["TOKEN"]);
        Assert.Equal(snapshot.Command, login.Command);
        Assert.Equal(snapshot.WorkingDirectory, login.WorkingDirectory);
    }
}

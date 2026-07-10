using System.Reflection;
using Xunit;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class ServerConfigurationMcpContractTests
{
    [Fact]
    public void ServerConfiguration_ProfileModel_Should_NotOwnMcpServers()
    {
        var mcpServersProperty = typeof(ServerConfiguration).GetProperty(
            "McpServers",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(mcpServersProperty);
    }
}

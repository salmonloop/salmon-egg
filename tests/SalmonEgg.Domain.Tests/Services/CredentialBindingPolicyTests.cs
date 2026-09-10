using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Domain.Tests.Services;

public sealed class CredentialBindingPolicyTests
{
    [Theory]
    [InlineData(CredentialSource.Token, CredentialTarget.Header, "https://agent.example/acp", "Host", null, CredentialBindingValidationError.InvalidHeaderName)]
    [InlineData(CredentialSource.Token, CredentialTarget.Header, "https://agent.example/acp", "Sec-WebSocket-Key", null, CredentialBindingValidationError.InvalidHeaderName)]
    [InlineData(CredentialSource.Token, CredentialTarget.Header, "https://agent.example/acp", "Authorization", "Bearer extra", CredentialBindingValidationError.InvalidHeaderScheme)]
    [InlineData(CredentialSource.Token, CredentialTarget.Header, "https://agent.example/acp#fragment", "Authorization", null, CredentialBindingValidationError.InvalidHeaderEndpoint)]
    [InlineData(CredentialSource.Token, CredentialTarget.Environment, "https://agent.example/acp", "AGENT_TOKEN", null, CredentialBindingValidationError.EnvironmentRequiresStdio)]
    [InlineData((CredentialSource)9, CredentialTarget.Header, "https://agent.example/acp", "Authorization", null, CredentialBindingValidationError.UnsupportedBinding)]
    [InlineData(CredentialSource.Token, (CredentialTarget)9, "https://agent.example/acp", "Authorization", null, CredentialBindingValidationError.UnsupportedBinding)]
    public void GetValidationError_InvalidHeaderBinding_ReturnsSemanticCategory(
        CredentialSource source, CredentialTarget target, string endpoint, string name, string? scheme, CredentialBindingValidationError expected)
    {
        // Arrange
        var configuration = new ServerConfiguration { Transport = TransportType.StreamableHttp, ServerUrl = endpoint };
        configuration.CredentialBinding = CredentialBindingPolicy.Create(configuration, source, target, name, scheme);

        // Act
        var result = CredentialBindingPolicy.GetValidationError(configuration);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("agent", "AGENT_TOKEN", null, null)]
    [InlineData("", "AGENT_TOKEN", null, CredentialBindingValidationError.EnvironmentRequiresStdio)]
    [InlineData("agent", "", null, CredentialBindingValidationError.InvalidEnvironmentName)]
    [InlineData("agent", "A=B", null, CredentialBindingValidationError.InvalidEnvironmentName)]
    [InlineData("agent", "AGENT_TOKEN\n", null, CredentialBindingValidationError.InvalidEnvironmentName)]
    [InlineData("agent", "PATH", null, CredentialBindingValidationError.ReservedEnvironmentName)]
    [InlineData("agent", "PathExt", null, CredentialBindingValidationError.ReservedEnvironmentName)]
    [InlineData("agent", "AGENT_TOKEN", "Bearer", CredentialBindingValidationError.EnvironmentHasScheme)]
    public void GetValidationError_EnvironmentBinding_PreservesValidationRules(
        string command, string name, string? scheme, CredentialBindingValidationError? expected)
    {
        // Arrange
        var configuration = new ServerConfiguration { Transport = TransportType.Stdio, StdioCommand = command };
        configuration.CredentialBinding = CredentialBindingPolicy.Create(configuration, CredentialSource.Token, CredentialTarget.Environment, name, scheme);

        // Act
        var result = CredentialBindingPolicy.GetValidationError(configuration);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetValidationError_DestinationChanged_RetainsApprovalBoundary()
    {
        // Arrange
        var configuration = new ServerConfiguration { Transport = TransportType.Stdio, StdioCommand = "agent" };
        Assert.Null(CredentialBindingPolicy.GetValidationError(configuration));
        configuration.CredentialBinding = CredentialBindingPolicy.Create(configuration, CredentialSource.Token, CredentialTarget.Environment, "AGENT_TOKEN");
        configuration.StdioCommand = "other-agent";

        // Act
        var result = CredentialBindingPolicy.GetValidationError(configuration);

        // Assert
        Assert.Equal(CredentialBindingValidationError.DestinationChanged, result);
        var error = Assert.IsType<CredentialBindingValidationError>(result);
        Assert.Contains("Bind the credential again", CredentialBindingPolicy.GetDiagnosticMessage(error), StringComparison.Ordinal);
    }
}

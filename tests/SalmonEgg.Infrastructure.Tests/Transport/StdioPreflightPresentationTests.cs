using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Transport;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioPreflightPresentationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_WhenConfiguredCommandIsMissing_PreservesPresentedFailure(bool searchedOnPath)
    {
        // Arrange
        var command = CreateMissingCommand(searchedOnPath);
        var formatter = new RecordingFormatter();
        using var transport = new StdioTransport(command, []);
        var factory = new AcpClientFactory(
            Mock.Of<IErrorLogger>(),
            new SessionManager(),
            new UnsupportedTerminalSessionManager(),
            formatter);
        using var client = factory.CreateClient(transport);
        var errors = new List<string>();
        client.ErrorOccurred += (_, error) => errors.Add(error);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(
            new InitializeParams(new ClientInfo("test", "1.0"), ClientCapabilityDefaults.Create()),
            TestContext.Current.CancellationToken));

        // Assert
        var failure = Assert.IsType<StdioCommandResolutionFailure>(formatter.LastError?.CommandResolutionFailure);
        Assert.Equal(command, failure.Command);
        Assert.Equal(searchedOnPath, failure.SearchedOnPath);
        Assert.Equal(formatter.PresentedMessage, Assert.Single(errors));
        Assert.Equal(formatter.PresentedMessage, exception.Message);
        Assert.False(transport.IsConnected);
    }

    [Theory]
    [InlineData(true, "not found on PATH")]
    [InlineData(false, "does not exist")]
    public async Task InitializeAsync_WithoutFormatter_PreservesActionableFallback(bool searchedOnPath, string expectedPhrase)
    {
        // Arrange
        var command = CreateMissingCommand(searchedOnPath);
        using var transport = new StdioTransport(command, []);
        using var adapter = new DomainAcpTransportAdapter(transport);
        using var client = new AcpClient(adapter);
        var errors = new List<string>();
        client.ErrorOccurred += (_, error) => errors.Add(error);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(
            new InitializeParams(new ClientInfo("test", "1.0"), ClientCapabilityDefaults.Create()),
            TestContext.Current.CancellationToken));

        // Assert
        var message = Assert.Single(errors);
        Assert.Contains(expectedPhrase, message, StringComparison.Ordinal);
        Assert.Contains(command, message, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh -t", message, StringComparison.Ordinal);
        Assert.Equal(message, exception.Message);
    }

    private static string CreateMissingCommand(bool searchedOnPath)
    {
        var command = "salmonegg-absent-agent-" + Guid.NewGuid().ToString("N");
        return searchedOnPath ? command : Path.Combine(Path.GetTempPath(), command);
    }

    private sealed class RecordingFormatter : ITransportErrorMessageFormatter
    {
        public TransportErrorEventArgs? LastError { get; private set; }

        public string? PresentedMessage { get; private set; }

        public string Format(TransportErrorEventArgs error)
        {
            LastError = error;
            PresentedMessage = error.CommandResolutionFailure is { } failure
                ? $"找不到命令“{failure.Command}”。请检查 Agent 配置。"
                : error.ErrorMessage;
            return PresentedMessage;
        }
    }
}

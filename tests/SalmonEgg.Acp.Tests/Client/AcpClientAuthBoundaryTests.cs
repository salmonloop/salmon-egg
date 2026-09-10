using System.Collections.Concurrent;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientAuthBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("Agent")]
    [InlineData("AGENT")]
    [InlineData("terminal")]
    [InlineData("_vendor_x")]
    [InlineData("future_thing")]
    public async Task AuthenticateAsync_RawUnsupportedDiscriminator_NeverSendsRequest(string methodType)
    {
        // Arrange
        using var peer = new RawAuthenticationPeer(CreateMethodJson(methodType));
        await peer.InitializeAsync();

        // Act
        var error = await Record.ExceptionAsync(() => peer.Client.AuthenticateAsync(
            new AuthenticateParams("login"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(peer.AuthenticationRequests);
        Assert.Equal(JsonRpcErrorCode.MethodNotAllowed, Assert.IsType<AcpException>(error).ErrorCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task InitializeAsync_RawNonStringDiscriminator_SkipsMethodWithoutAuthenticating(string rawType)
    {
        // Arrange
        using var peer = new RawAuthenticationPeer(
            $$"""{"id":"login","name":"Login","type":{{rawType}}}""");

        // Act
        var response = await peer.InitializeAsync();
        var authenticateError = await Record.ExceptionAsync(() => peer.Client.AuthenticateAsync(
            new AuthenticateParams("login"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(peer.AuthenticationRequests);
        Assert.Empty(response.AuthMethods!);
        Assert.IsType<AcpException>(authenticateError);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task AuthenticateAsync_InvalidMethodBeforeAgent_StillSendsOnlyTheValidMethod(string rawType)
    {
        // Arrange
        using var peer = new RawAuthenticationPeer(
            $$"""{"id":"bad","name":"Bad","type":{{rawType}}},{"id":"login","name":"Login","type":"agent"}""");
        var initialized = await peer.InitializeAsync();

        // Act
        await peer.Client.AuthenticateAsync(new AuthenticateParams("login"), TestContext.Current.CancellationToken);
        var invalidError = await Record.ExceptionAsync(() => peer.Client.AuthenticateAsync(
            new AuthenticateParams("bad"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("login", Assert.Single(initialized.AuthMethods!).Id);
        using var request = JsonDocument.Parse(Assert.Single(peer.AuthenticationRequests));
        Assert.Equal("login", request.RootElement.GetProperty("params").GetProperty("methodId").GetString());
        Assert.Equal(JsonRpcErrorCode.InvalidParams, Assert.IsType<AcpException>(invalidError).ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("agent")]
    public async Task AuthenticateAsync_RawDefaultOrAgentDiscriminator_SendsAdvertisedMethod(string? methodType)
    {
        // Arrange
        using var peer = new RawAuthenticationPeer(CreateMethodJson(methodType));
        await peer.InitializeAsync();

        // Act
        var response = await peer.Client.AuthenticateAsync(
            new AuthenticateParams("login"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        using var request = JsonDocument.Parse(Assert.Single(peer.AuthenticationRequests));
        Assert.Equal("login", request.RootElement.GetProperty("params").GetProperty("methodId").GetString());
    }

    [Fact]
    public async Task AuthenticateAsync_V1MethodWithUnknownMethodIdField_SendsAuthoritativeId()
    {
        // Arrange
        using var peer = new RawAuthenticationPeer(
            """{"id":"login","methodId":"other","name":"Login","type":"agent"}""");
        await peer.InitializeAsync();

        // Act
        var response = await peer.Client.AuthenticateAsync(
            new AuthenticateParams("login"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        using var request = JsonDocument.Parse(Assert.Single(peer.AuthenticationRequests));
        Assert.Equal("login", request.RootElement.GetProperty("params").GetProperty("methodId").GetString());
    }

    private static string CreateMethodJson(string? type)
        => type is null
            ? """{"id":"login","name":"Login"}"""
            : $$"""{"id":"login","name":"Login","type":"{{JsonEncodedText.Encode(type)}}"}""";

    private sealed class RawAuthenticationPeer : IDisposable
    {
        private readonly Mock<IAcpTransport> _transport = new();

        public RawAuthenticationPeer(string methodJson)
        {
            _transport.SetupGet(transport => transport.IsConnected).Returns(true);
            _transport
                .Setup(transport => transport.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((message, _) =>
                {
                    using var request = JsonDocument.Parse(message);
                    var root = request.RootElement;
                    var method = root.GetProperty("method").GetString();
                    var result = "{}";
                    if (method == "initialize")
                    {
                        // Raw peer JSON bypasses the SDK writer that could otherwise normalize the failing input.
                        result = $$"""{"protocolVersion":1,"agentCapabilities":{},"authMethods":[{{methodJson}}]}""";
                    }
                    else if (method == "authenticate")
                    {
                        AuthenticationRequests.Enqueue(message);
                    }

                    var response = $$"""{"jsonrpc":"2.0","id":{{root.GetProperty("id").GetRawText()}},"result":{{result}}}""";
                    _transport.Raise(
                        transport => transport.MessageReceived += null,
                        new AcpTransportMessageReceivedEventArgs(response));
                    return Task.FromResult(true);
                });

            Client = new AcpClient(_transport.Object, Mock.Of<IAcpClientLogger>());
        }

        public AcpClient Client { get; }

        public ConcurrentQueue<string> AuthenticationRequests { get; } = new();

        public Task<InitializeResponse> InitializeAsync()
            => Client.InitializeAsync(
                new InitializeParams(new ClientInfo("test", "1.0"), new ClientCapabilities()),
                TestContext.Current.CancellationToken);

        public void Dispose() => Client.Dispose();
    }
}

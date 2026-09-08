using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Interactions;

public sealed class ChatElicitationRoutingTests
{
    [Theory]
    [InlineData("request")]
    [InlineData("unbound-session")]
    [InlineData("routing-failure")]
    public async Task BuildElicitationRequestAsync_WhenFormCannotBeShown_SendsOneCancelResponse(string scenario)
    {
        var transport = new Mock<IAcpTransport>();
        var responses = new List<JsonElement>();
        SetupTransport(transport, responses);
        using var client = new AcpClient(transport.Object, Mock.Of<IAcpClientLogger>());
        using var service = new ChatService(client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>());
        await client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"), ClientCapabilityDefaults.Create()), TestContext.Current.CancellationToken);
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        if (scenario == "routing-failure")
        {
            router.Setup(x => x.ResolveConversationIdAsync("remote", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Route unavailable"));
        }

        var bridge = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator());
        Task<(string ConversationId, ElicitationRequestViewModel ViewModel)?>? projection = null;
        ElicitationRequestEventArgs? received = null;
        service.ElicitationRequestReceived += (_, args) =>
        {
            received = args;
            projection = bridge.BuildElicitationRequestAsync(args, (_, _) => Task.CompletedTask, NullLogger.Instance);
        };
        var scope = scenario == "request" ? "\"requestId\":12" : "\"sessionId\":\"remote\"";
        transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
            $$$"""{"jsonrpc":"2.0","id":"form-1","method":"elicitation/create","params":{ {{{scope}}},"mode":"form","message":"Choose","requestedSchema":{"type":"object","properties":{}} }}"""));

        Assert.NotNull(projection);
        Assert.Null(await projection.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var response = Assert.Single(responses);
        Assert.Equal("form-1", response.GetProperty("id").GetString());
        Assert.False(response.TryGetProperty("error", out _));
        Assert.Equal("{\"action\":\"cancel\"}", response.GetProperty("result").GetRawText());
        Assert.NotNull(received);
        Assert.False(await received.Cancel());
        Assert.Single(responses);
    }

    [Fact]
    public async Task BuildElicitationRequestAsync_WhenConversationResolves_WaitsForUserAndClearsAfterResponse()
    {
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        router.Setup(x => x.ResolveConversationIdAsync("remote", It.IsAny<CancellationToken>())).ReturnsAsync("conversation");
        var bridge = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator());
        var accepted = 0;
        var cancelled = 0;
        string? cleared = null;
        ElicitationRequestViewModel? clearedRequest = null;
        var args = new ElicitationRequestEventArgs("form-1", new FormElicitationRequest
        {
            Scope = ElicitationScope.ForSession("remote"),
            Message = "Choose"
        }, _ => { accepted++; return Task.FromResult(true); }, () => Task.FromResult(true),
            () => { cancelled++; return Task.FromResult(true); });

        var result = await bridge.BuildElicitationRequestAsync(args,
            (id, request) => { cleared = id; clearedRequest = request; return Task.CompletedTask; }, NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("conversation", result.Value.ConversationId);
        Assert.Equal(0, accepted);
        Assert.Equal(0, cancelled);
        Assert.Null(cleared);
        await result.Value.ViewModel.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(1, accepted);
        Assert.Equal("conversation", cleared);
        Assert.Same(result.Value.ViewModel, clearedRequest);
    }

    private static void SetupTransport(Mock<IAcpTransport> transport, List<JsonElement> responses)
    {
        transport.SetupGet(t => t.IsConnected).Returns(true);
        transport.Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((message, cancellationToken) =>
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.TryGetProperty("method", out var method) && method.GetString() == "initialize")
                {
                    var id = root.GetProperty("id").GetRawText();
                    transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                        $$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"result":{"protocolVersion":1,"agentInfo":{"name":"Test agent","version":"1"},"agentCapabilities":{}}}"""));
                }
                else if (root.TryGetProperty("id", out _) && !root.TryGetProperty("method", out _))
                {
                    responses.Add(root.Clone());
                }
            }).ReturnsAsync(true);
    }
}

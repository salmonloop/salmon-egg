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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildElicitationRequestAsync_UrlCompletionBeforeOrAfterConsent_UsesSameRequestState(bool earlyCompletion)
    {
        // Arrange
        var transport = new Mock<IAcpTransport>();
        var responses = new List<JsonElement>();
        SetupTransport(transport, responses);
        using var client = new AcpClient(transport.Object, Mock.Of<IAcpClientLogger>());
        var capabilities = ClientCapabilityDefaults.Create() with
        {
            Elicitation = new ElicitationCapabilities { Form = new(), Url = new() }
        };
        await client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"), capabilities), TestContext.Current.CancellationToken);
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        router.Setup(x => x.ResolveConversationIdAsync("remote", It.IsAny<CancellationToken>())).ReturnsAsync("conversation");
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(x => x.IsSupported).Returns(true);
        launcher.Setup(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>())).ReturnsAsync(ExternalUriOpenResult.Opened);
        var bridge = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator(), uriLauncher: launcher.Object);
        ElicitationRequestEventArgs? args = null;
        client.ElicitationRequestReceived += (_, value) => args = value;
        transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
            """{"jsonrpc":"2.0","id":201,"method":"elicitation/create","params":{"sessionId":"remote","mode":"url","elicitationId":"opaque","url":"https://example.com/authorize?canary=private","message":"Approve access"}}"""));
        Assert.NotNull(args);
        if (earlyCompletion)
        {
            Complete();
        }

        // Act
        var projection = await bridge.BuildElicitationRequestAsync(args, (_, _) => Task.CompletedTask, NullLogger.Instance);
        using var request = projection!.Value.ViewModel;

        // Assert
        launcher.Verify(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(request.CanSubmit);
        Assert.Equal(earlyCompletion, request.IsCompleted);
        await request.SubmitCommand.ExecuteAsync(null);
        Assert.Equal("{\"action\":\"accept\"}", Assert.Single(responses).GetProperty("result").GetRawText());
        Complete();
        Complete();
        Assert.True(request.IsCompleted);
        Assert.True(request.IsAwaitingCompletion);
        await request.ReopenCommand.ExecuteAsync(null);
        launcher.Verify(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Once);

        void Complete() => transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
            """{"jsonrpc":"2.0","method":"elicitation/complete","params":{"elicitationId":"opaque"}}"""));
    }

    [Fact]
    public async Task BuildElicitationRequestAsync_DisconnectedUrl_NeverOpens()
    {
        // Arrange
        var transport = new Mock<IAcpTransport>();
        var responses = new List<JsonElement>();
        SetupTransport(transport, responses);
        using var client = new AcpClient(transport.Object, Mock.Of<IAcpClientLogger>());
        await client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"),
            new ClientCapabilities { Elicitation = new() { Url = new() } }), TestContext.Current.CancellationToken);
        ElicitationRequestEventArgs? args = null;
        client.ElicitationRequestReceived += (_, value) => args = value;
        transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
            """{"jsonrpc":"2.0","id":202,"method":"elicitation/create","params":{"sessionId":"remote","mode":"url","elicitationId":"opaque","url":"https://example.com/authorize?canary=private","message":"Approve access"}}"""));
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(x => x.IsSupported).Returns(true);
        using var request = ElicitationInteractionViewModelFactory.Create(args!, _ => Task.CompletedTask, uriLauncher: launcher.Object);
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(request.FullUrl) && request.FullUrl.Length == 0)
            {
                cleared.TrySetResult();
            }
        };

        // Act
        await client.DisconnectAsync();
        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.False(request.CanSubmit);
        Assert.Empty(request.FullUrl);
        Assert.Empty(responses);
        launcher.Verify(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildElicitationRequestAsync_OldConnectionReusesIds_DoesNotOpenOrCompleteNewRequest()
    {
        // Arrange
        var oldTransport = new Mock<IAcpTransport>();
        var newTransport = new Mock<IAcpTransport>();
        var oldResponses = new List<JsonElement>();
        var newResponses = new List<JsonElement>();
        SetupTransport(oldTransport, oldResponses);
        SetupTransport(newTransport, newResponses);
        using var oldClient = new AcpClient(oldTransport.Object);
        using var newClient = new AcpClient(newTransport.Object);
        var capabilities = new ClientCapabilities { Elicitation = new() { Url = new() } };
        await oldClient.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"), capabilities), TestContext.Current.CancellationToken);
        await newClient.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"), capabilities), TestContext.Current.CancellationToken);
        ElicitationRequestEventArgs? oldArgs = null;
        ElicitationRequestEventArgs? newArgs = null;
        oldClient.ElicitationRequestReceived += (_, args) => oldArgs = args;
        newClient.ElicitationRequestReceived += (_, args) => newArgs = args;
        const string message = """{"jsonrpc":"2.0","id":203,"method":"elicitation/create","params":{"sessionId":"remote","mode":"url","elicitationId":"opaque","url":"https://example.com/authorize","message":"Approve access"}}""";
        oldTransport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(message));
        newTransport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(message));
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(x => x.IsSupported).Returns(true);
        launcher.Setup(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>())).ReturnsAsync(ExternalUriOpenResult.Dispatched);
        using var previous = ElicitationInteractionViewModelFactory.Create(oldArgs!, _ => Task.CompletedTask, uriLauncher: launcher.Object);
        using var current = ElicitationInteractionViewModelFactory.Create(newArgs!, _ => Task.CompletedTask, uriLauncher: launcher.Object);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.PropertyChanged += OnPreviousChanged;

        void OnPreviousChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(previous.FullUrl) && previous.FullUrl.Length == 0) expired.TrySetResult();
        }

        // Act
        try
        {
            await oldClient.DisconnectAsync();
            await expired.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            previous.PropertyChanged -= OnPreviousChanged;
        }
        oldTransport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
            """{"jsonrpc":"2.0","method":"elicitation/complete","params":{"elicitationId":"opaque"}}"""));
        await previous.SubmitCommand.ExecuteAsync(null);
        await current.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(previous.FullUrl);
        Assert.Empty(oldResponses);
        Assert.Equal("{\"action\":\"accept\"}", Assert.Single(newResponses).GetProperty("result").GetRawText());
        Assert.True(current.IsAwaitingCompletion);
        Assert.False(current.IsCompleted);
        launcher.Verify(x => x.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, "", false)]
    [InlineData(false, "", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "  ", false)]
    public async Task BuildElicitationRequestAsync_MultiSelectLabels_PreservesWireValues(bool titled, string blankTitle, bool omitItemType)
    {
        // Arrange
        var transport = new Mock<IAcpTransport>();
        var responses = new List<JsonElement>();
        SetupTransport(transport, responses);
        using var client = new AcpClient(transport.Object, Mock.Of<IAcpClientLogger>());
        using var service = new ChatService(client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>());
        await client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"), ClientCapabilityDefaults.Create()), TestContext.Current.CancellationToken);
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        router.Setup(x => x.ResolveConversationIdAsync("remote", It.IsAny<CancellationToken>())).ReturnsAsync("conversation");
        var bridge = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator());
        var received = new TaskCompletionSource<ElicitationRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRequest(object? sender, ElicitationRequestEventArgs args) => received.TrySetResult(args);
        service.ElicitationRequestReceived += OnRequest;
        var items = titled
            ? $$$"""{"anyOf":[{"const":"api-internal","title":"Public API","description":"Read interface contracts"},{"const":"ui-internal","title":"Desktop UI","description":null},{"const":"fallback-internal","title":"{{{blankTitle}}}","description":"  "}]}"""
            : omitItemType
                ? """{"enum":["api-internal","ui-internal","fallback-internal"]}"""
                : """{"type":"string","enum":["api-internal","ui-internal","fallback-internal"]}""";
        ElicitationRequestViewModel? clearedRequest = null;
        try
        {
            // Act: the displayed values originate in a real parsed ACP request.
            transport.Raise(t => t.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                $$$$"""{"jsonrpc":"2.0","id":"titled-form","method":"elicitation/create","params":{"sessionId":"remote","mode":"form","message":"Choose targets","requestedSchema":{"type":"object","properties":{"targets":{"type":"array","items":{{{{items}}}},"default":["api-internal"],"minItems":1}},"required":["targets"]}}} """));
            var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var projection = await bridge.BuildElicitationRequestAsync(request,
                (_, current) => { clearedRequest = current; return Task.CompletedTask; }, NullLogger.Instance);

            // Assert: labels and descriptions are display data; defaults still match const values.
            Assert.NotNull(projection);
            var viewModel = projection.Value.ViewModel;
            var field = Assert.IsType<ElicitationMultiSelectFieldViewModel>(Assert.Single(viewModel.Fields));
            Assert.Equal(titled ? ["Public API", "Desktop UI", "fallback-internal"]
                : new[] { "api-internal", "ui-internal", "fallback-internal" }, field.Options.Select(option => option.DisplayName));
            Assert.Equal(titled ? "Read interface contracts" : string.Empty, field.Options[0].Description);
            Assert.Equal(titled, field.Options[0].HasDescription);
            Assert.False(field.Options[1].HasDescription);
            Assert.False(field.Options[2].HasDescription);
            Assert.Equal([true, false, false], field.Options.Select(option => option.IsSelected));
            Assert.Empty(responses);
            field.Options[0].IsSelected = false;
            field.Options[1].IsSelected = true;
            field.Options[2].IsSelected = true;
            await viewModel.SubmitCommand.ExecuteAsync(null);
            var response = Assert.Single(responses);
            Assert.Equal("titled-form", response.GetProperty("id").GetString());
            Assert.Equal("accept", response.GetProperty("result").GetProperty("action").GetString());
            Assert.Equal(["ui-internal", "fallback-internal"], response.GetProperty("result")
                .GetProperty("content").GetProperty("targets").EnumerateArray().Select(value => value.GetString()));
            Assert.Same(viewModel, clearedRequest);
        }
        finally
        {
            service.ElicitationRequestReceived -= OnRequest;
        }
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

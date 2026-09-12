using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpConfigurationUpdateViewTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task SessionConfiguration_UnknownOptionBesideGroupedChoice_KeepsKnownRowsInTheApplicationView(int version, bool responseProjection)
    {
        // Arrange: the same grouped/unknown mix used by the real browser gate, under either wire version.
        var optionsJson = """
            [{"id":"response-style","name":"Response style","description":"Choose the detail used in this conversation.",
              "type":"select","currentValue":"short","options":[{"group":"standard","name":"Standard",
              "options":[{"value":"short","name":"Brief"}]}]},
             {"id":"future-setting","name":"Future knob","type":"future","currentValue":{"opaque":1e2}}]
            """;
        if (version == 2) optionsJson = optionsJson.Replace("\"id\":", "\"configId\":").Replace("\"group\":", "\"groupId\":");
        var transport = new Mock<IAcpTransport>();
        transport.SetupGet(value => value.IsConnected).Returns(true);
        transport.Setup(value => value.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        transport.Setup(value => value.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                using var request = JsonDocument.Parse(message);
                var root = request.RootElement;
                var result = root.GetProperty("method").ValueEquals("initialize")
                    ? "{\"protocolVersion\":" + version + ",\"agentCapabilities\":{},\"info\":{\"name\":\"fixture\",\"version\":\"1\"}}"
                    : "{\"configOptions\":" + optionsJson + "}";
                transport.Raise(value => value.MessageReceived += null,
                    new AcpTransportMessageReceivedEventArgs("{\"jsonrpc\":\"2.0\",\"id\":"
                        + root.GetProperty("id").GetRawText() + ",\"result\":" + result + "}"));
                return Task.FromResult(true);
            });
        using var client = new AcpClient(transport.Object, null, null, null,
            new AcpClientOptions { ExperimentalProtocolVersions = [2] });
        await client.InitializeAsync(new(new("config-view-test", "1"), new()) { ProtocolVersion = version }, TestContext.Current.CancellationToken);
        var received = new TaskCompletionSource<SessionUpdateEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionUpdateReceived += (_, update) => received.TrySetResult(update);
        // Act / Assert: retaining an unknown sibling must not break the whole application projection.
        if (responseProjection)
            await client.SetSessionConfigOptionAsync(new("remote", "response-style", "short"), TestContext.Current.CancellationToken);
        else
            transport.Raise(value => value.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"remote\",\"update\":"
                + "{\"sessionUpdate\":\"config_option_update\",\"configOptions\":" + optionsJson + "}}}"));
        var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var options = Assert.IsType<AcpSessionUpdateView>(update.View).ConfigOptions;
        Assert.Equal(2, options.Length);
        Assert.Equal("response-style", options[0].Id);
        Assert.Equal("Choose the detail used in this conversation.", options[0].Description);
        Assert.Equal("standard", Assert.Single(options[0].OptionGroups).Group);
        Assert.Equal("future-setting", options[1].Id);
        Assert.Equal("future", options[1].Type);
        Assert.Equal(responseProjection, update.IsResponseProjection);
        Assert.True(options[1].RawPayload!.Value.TryGetProperty(version == 1 ? "id" : "configId", out _));
        Assert.Equal("1e2", options[1].RawPayload!.Value.GetProperty("currentValue").GetProperty("opaque").GetRawText());
        options[0].OptionGroups[0].Options.Clear();
        Assert.Single(update.View!.ConfigOptions[0].OptionGroups[0].Options);
    }
}

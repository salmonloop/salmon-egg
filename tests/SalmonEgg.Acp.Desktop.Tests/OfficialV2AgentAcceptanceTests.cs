#pragma warning disable SEACP002 // This opt-in test exercises the upstream draft Agent; product defaults stay on v1.
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Logging;
using SalmonEgg.Application.Services.Chat;
using System.Collections.Concurrent;
using Xunit;

namespace SalmonEgg.Acp.Desktop.Tests;

public sealed class OfficialV2AgentAcceptanceTests
{
    [Fact]
    public async Task OfficialV2Agent_PublicExperimentalClientAndChatService_ProjectMessagesAndRestoreHistory()
    {
        var command = Environment.GetEnvironmentVariable("SALMONEGG_OFFICIAL_V2_AGENT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(command), "Build the pinned official Rust simple_agent_v2 example.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        using var transport = new StdioTransport(command!);
        using var client = new AcpClient(new DomainAcpTransportAdapter(transport), null, null, null,
            new AcpClientOptions { ExperimentalProtocolVersions = [AcpProtocolVersion.V2] });
        using var service = new ChatService(client, new ErrorLogger(), new SessionManager());
        var updates = new ConcurrentQueue<SessionUpdateEventArgs>();
        service.SessionUpdateReceived += (_, update) => updates.Enqueue(update);
        await service.InitializeAsync(new InitializeParams(new ClientInfo("salmon-egg-app-interop", "1"), new ClientCapabilities())
        { ProtocolVersion = AcpProtocolVersion.V2 }).WaitAsync(token);
        var directory = Path.GetFullPath(Path.GetTempPath());
        var session = await service.CreateSessionAsync(new SessionNewParams(directory, [])).WaitAsync(token);
        var marker = "application-v2-" + Guid.NewGuid().ToString("N");

        var prompt = await service.SendPromptAsync(new SessionPromptParams(session.SessionId, [new TextContentBlock(marker)]), token);
        await service.CloseSessionAsync(new SessionCloseParams(session.SessionId), token);
        await service.LoadSessionAsync(new SessionLoadParams(session.SessionId, directory), token);

        Assert.Equal(2, service.NegotiatedProtocolVersion);
        Assert.Equal(StopReason.EndTurn, prompt.StopReason);
        Assert.Contains(updates, update => update.View?.Message?.Content.OfType<TextContentBlock>()
            .Any(content => content.Text.Contains(marker, StringComparison.Ordinal)) == true);
        Assert.Contains(updates, update => update.View?.WorkState == "idle");
        Assert.True(await client.DisconnectAsync());
    }

    [Fact]
    public async Task OfficialV2Agent_PromptCloseResume_UsesAuthoritativeHistory()
    {
        // Arrange
        var command = Environment.GetEnvironmentVariable("SALMONEGG_OFFICIAL_V2_AGENT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(command), "Build the pinned official Rust simple_agent_v2 example and supply its executable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        using var transport = new StdioTransport(command!);
        using var client = new AcpClient(new DomainAcpTransportAdapter(transport), null, null, null,
            new AcpClientOptions { ExperimentalProtocolVersions = [AcpProtocolVersion.V2] });
        await client.InitializeAsync(new InitializeParams(new ClientInfo("salmon-egg-v2-interop", "1"), new ClientCapabilities())
        { ProtocolVersion = AcpProtocolVersion.V2 }, token);
        var directory = Path.GetFullPath(Path.GetTempPath());
        var session = await client.CreateSessionAsync(new SessionNewParams(directory, []), token);
        var marker = "official-v2-" + Guid.NewGuid().ToString("N");

        // Act
        var response = await client.SendPromptAsync(new SessionPromptParams(session.SessionId,
            [new TextContentBlock(marker)]), token);
        var before = client.GetSessionSnapshot(session.SessionId);
        await client.CloseSessionAsync(new SessionCloseParams(session.SessionId), token);
        await client.ResumeSessionAsync(new SessionResumeParams(session.SessionId, directory, [], replayFrom: SessionReplayFrom.Start), token);
        var after = client.GetSessionSnapshot(session.SessionId);

        // Assert: actual upstream replay must rebuild the same messages exactly once.
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.True(response.HasStopReason);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEmpty(before.Messages);
        Assert.Equal(before.Messages.Select(message => message.MessageId), after.Messages.Select(message => message.MessageId));
        Assert.Contains(after.Messages, message => message.Content.OfType<TextContentBlock>().Any(content => content.Text.Contains(marker, StringComparison.Ordinal)));
        Assert.True(await client.DisconnectAsync());
    }

}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class ChatServiceSessionTests
{
    [Fact]
    public async Task SendPromptAsync_ForwardsCancellationToken()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        CancellationToken captured = default;
        acpClient
            .Setup(c => c.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionPromptParams, CancellationToken>((_, ct) => captured = ct)
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        using var cts = new CancellationTokenSource();
        await sut.SendPromptAsync(new SessionPromptParams("s1", prompt: new List<ContentBlock>()), cts.Token);

        Assert.Equal(cts.Token, captured);

        sut.Dispose();
    }

    [Fact]
    public void SessionUpdate_IsStoredPerSessionId()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        var update = new AgentMessageUpdate(new TextContentBlock("hello"));
        acpClient.Raise(
            c => c.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("s1", update));

        var session = sessionManager.GetSession("s1");
        Assert.NotNull(session);
        Assert.Single(session!.History);
        Assert.IsType<TextContentBlock>(session.History[0].Content);
        Assert.Equal("hello", ((TextContentBlock)session.History[0].Content!).Text);

        sut.Dispose();
    }

    [Fact]
    public async Task LoadSessionAsync_WhenClientThrows_RestoresCachedHistoryAndPreviousSession()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        // Seed current session via CreateSessionAsync.
        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.CreateSessionAsync(It.IsAny<SessionNewParams>(), default))
            .ReturnsAsync(new SessionNewResponse { SessionId = "s1" });

        // Loading a different session fails.
        acpClient
            .Setup(c => c.LoadSessionAsync(It.IsAny<SessionLoadParams>(), default))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.CreateSessionAsync(new SessionNewParams { Cwd = Environment.CurrentDirectory });
        Assert.Equal("s1", sut.CurrentSessionId);

        // Seed cached history for the target session.
        await sessionManager.CreateSessionAsync("s2", cwd: Environment.CurrentDirectory);
        sessionManager.UpdateSession("s2", s => s.AddHistoryEntry(SalmonEgg.Domain.Models.Session.SessionUpdateEntry.CreateMessage(new TextContentBlock("cached"))));

        var before = sessionManager.GetSession("s2")!.History.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.LoadSessionAsync(new SessionLoadParams("s2", Environment.CurrentDirectory)));

        Assert.Equal("s1", sut.CurrentSessionId);
        Assert.Equal(before, sessionManager.GetSession("s2")!.History.Count);

        sut.Dispose();
    }
}

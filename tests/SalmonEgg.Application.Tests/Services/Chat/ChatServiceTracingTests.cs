using System.Diagnostics;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Observability;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class ChatServiceTracingTests
{
    [Fact]
    public async Task InitializeAsync_EmitsBusinessOperationSpan()
    {
        // Arrange
        var acpClient = new Mock<IAcpClient>();
        acpClient
            .Setup(client => client.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(
                AcpProtocolVersion.V1,
                new AgentInfo("TestAgent", "1.0.0"),
                new AgentCapabilities()));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.initialize").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await service.InitializeAsync(new InitializeParams(
            new ClientInfo("Test", "1.0.0"),
            new ClientCapabilities()));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("chat.initialize", activity.DisplayName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task SendPromptAsync_CallerCancels_LeavesBusinessSpanNonError()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var acpClient = new Mock<IAcpClient>();
        acpClient
            .Setup(client => client.SendPromptAsync(
                It.IsAny<SessionPromptParams>(),
                cancellation.Token))
            .Returns(Task.FromCanceled<SessionPromptResponse>(cancellation.Token));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.prompt.cancelled").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendPromptAsync(
            new SessionPromptParams("session-1", []),
            cancellation.Token));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("chat.session.prompt", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.False(activity.Events.Any());
    }

    private static ActivityListener CreateListener(
        ActivityTraceId traceId,
        ICollection<Activity> stoppedActivities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationActivitySources.ChatServiceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    stoppedActivities.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}

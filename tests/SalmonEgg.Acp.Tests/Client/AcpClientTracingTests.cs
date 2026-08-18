using System.Diagnostics;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Observability;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientTracingTests
{
    [Fact]
    public async Task InitializeAsync_Success_EmitsRecordedClientSpanWithoutPayloadData()
    {
        // Arrange
        var transport = CreateConnectedTransport();
        var parser = new MessageParser();
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.acp.success").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var client = new AcpClient(transport.Object);
        var response = new InitializeResponse(
            AcpProtocolVersion.V1,
            new AgentInfo("TestAgent", "1.0.0"),
            new AgentCapabilities());

        SetupResponse(
            transport,
            parser,
            JsonSerializer.SerializeToElement(response, AcpJsonContext.Default.InitializeResponse));

        // Act
        await client.InitializeAsync(
            new InitializeParams(
                new ClientInfo("private-client-name", "1.0.0"),
                new ClientCapabilities()),
            TestContext.Current.CancellationToken);

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("acp.request initialize", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("jsonrpc", GetTag(activity, "rpc.system"));
        Assert.Equal("initialize", GetTag(activity, "rpc.method"));
        Assert.DoesNotContain(
            activity.TagObjects.Select(static pair => pair.Value?.ToString()),
            static value => value?.Contains("private-client-name", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task InitializeAsync_SendFailure_EmitsErrorSpanWithExceptionEvent()
    {
        // Arrange
        var transport = CreateConnectedTransport();
        transport
            .Setup(candidate => candidate.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.acp.failure").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var client = new AcpClient(transport.Object);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(
            new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                new ClientCapabilities()),
            TestContext.Current.CancellationToken));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, GetTag(activity, "error.type"));
        var exceptionEvent = Assert.Single(activity.Events);
        Assert.Equal("exception", exceptionEvent.Name);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            exceptionEvent.Tags.Single(tag => tag.Key == "exception.type").Value);
    }

    [Fact]
    public async Task InitializeAsync_CallerCancels_EmitsNonErrorCancelledSpan()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = CreateConnectedTransport();
        transport
            .Setup(candidate => candidate.SendMessageAsync(
                It.IsAny<string>(),
                cancellation.Token))
            .Returns(Task.FromCanceled<bool>(cancellation.Token));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.acp.cancelled").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var client = new AcpClient(transport.Object);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InitializeAsync(
            new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                new ClientCapabilities()),
            cancellation.Token));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(true, GetTag(activity, "acp.request.cancelled"));
        Assert.False(activity.Events.Any());
    }

    private static Mock<IAcpTransport> CreateConnectedTransport()
    {
        var transport = new Mock<IAcpTransport>();
        transport.SetupGet(candidate => candidate.IsConnected).Returns(true);
        return transport;
    }

    private static ActivityListener CreateListener(
        ActivityTraceId traceId,
        ICollection<Activity> stoppedActivities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AcpActivitySources.ClientName,
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

    private static object? GetTag(Activity activity, string key)
        => activity.TagObjects.Single(pair => pair.Key == key).Value;

    private static void SetupResponse(
        Mock<IAcpTransport> transport,
        MessageParser parser,
        JsonElement result)
    {
        transport
            .Setup(candidate => candidate.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                var request = parser.ParseRequest(message);
                var response = new JsonRpcResponse(request.Id, result);
                transport.Raise(
                    candidate => candidate.MessageReceived += null,
                    new AcpTransportMessageReceivedEventArgs(parser.SerializeMessage(response)));
                return Task.FromResult(true);
            });
    }
}

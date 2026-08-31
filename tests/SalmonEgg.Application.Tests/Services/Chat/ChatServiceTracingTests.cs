using System.Diagnostics;
using System.Diagnostics.Metrics;
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

        // Assert：取消时 span 名退化为裸操作名（mock 未设 AgentInfo），状态保持 Unset。
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(ApplicationSemanticConventions.GenAi.InvokeAgentOperation, activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.False(activity.Events.Any());
    }

    [Fact]
    public async Task SendPromptAsync_Success_EmitsInvokeAgentSpanWithGenAiAttributes()
    {
        // Arrange
        var acpClient = new Mock<IAcpClient>();
        acpClient.Setup(client => client.AgentInfo)
            .Returns(new AgentInfo("TestAgent", "2.3.4"));
        acpClient
            .Setup(client => client.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.prompt.success").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await service.SendPromptAsync(new SessionPromptParams("session-42", []));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("invoke_agent TestAgent", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(
            ApplicationSemanticConventions.GenAi.InvokeAgentOperation,
            activity.GetTagItem(ApplicationSemanticConventions.GenAi.OperationName));
        Assert.Equal(
            "TestAgent",
            activity.GetTagItem(ApplicationSemanticConventions.GenAi.AgentName));
        Assert.Equal(
            "2.3.4",
            activity.GetTagItem(ApplicationSemanticConventions.GenAi.AgentVersion));
        Assert.Equal(
            "session-42",
            activity.GetTagItem(ApplicationSemanticConventions.GenAi.ConversationId));
        // 封闭枚举的 provider 与无数据源的 usage/model 键必须缺席（见 ApplyInvokeAgentAttributes 备注）。
        Assert.Null(activity.GetTagItem("gen_ai.provider.name"));
        Assert.Null(activity.GetTagItem("gen_ai.request.model"));
        Assert.Null(activity.GetTagItem("gen_ai.usage.input_tokens"));
    }

    [Fact]
    public async Task SendPromptAsync_UnknownAgent_UsesBareSpanNameAndSkipsAgentTags()
    {
        // Arrange：AgentInfo 为 null（未完成 initialize）。
        var acpClient = new Mock<IAcpClient>();
        acpClient
            .Setup(client => client.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.prompt.unknown-agent").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await service.SendPromptAsync(new SessionPromptParams("session-7", []));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(ApplicationSemanticConventions.GenAi.InvokeAgentOperation, activity.DisplayName);
        Assert.Null(activity.GetTagItem(ApplicationSemanticConventions.GenAi.AgentName));
        // sessionId 存在，conversation.id 照发——它不依赖 AgentInfo。
        Assert.NotNull(activity.GetTagItem(ApplicationSemanticConventions.GenAi.ConversationId));
    }

    [Fact]
    public async Task SendPromptAsync_EmptySessionId_OmitsConversationId()
    {
        // Arrange：规范禁止用 UUID / traceId / 哈希兜底 conversation.id。
        var acpClient = new Mock<IAcpClient>();
        acpClient
            .Setup(client => client.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.prompt.no-session").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await service.SendPromptAsync(new SessionPromptParams(string.Empty, []));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Null(activity.GetTagItem(ApplicationSemanticConventions.GenAi.ConversationId));
    }

    [Fact]
    public async Task SendPromptAsync_Failure_MarksSpanErrorAndTagsCanonicalErrorType()
    {
        // Arrange
        var acpClient = new Mock<IAcpClient>();
        acpClient.Setup(client => client.AgentInfo)
            .Returns(new AgentInfo("TestAgent", "2.3.4"));
        acpClient
            .Setup(client => client.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var stoppedActivities = new List<Activity>();
        using var parent = new Activity("test.chat.prompt.failure").Start();
        using var listener = CreateListener(parent.TraceId, stoppedActivities);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendPromptAsync(
            new SessionPromptParams("session-9", [])));

        // Assert
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            activity.GetTagItem(OtelErrorAttributes.Type));
        Assert.True(activity.Events.Any(@event => @event.Name == "exception"));
    }

    [Fact]
    public async Task SendPromptAsync_Success_RecordsDurationWithAgentDimensionOnly()
    {
        // Arrange：成功样本只带 gen_ai.agent.name 维度；error.type 必须缺席，
        // 否则「有没有出错」的查询会失真。
        var acpClient = new Mock<IAcpClient>();
        acpClient.Setup(client => client.AgentInfo)
            .Returns(new AgentInfo("TestAgent", "2.3.4"));
        acpClient
            .Setup(client => client.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));
        using var parent = new Activity("test.chat.metric.success").Start();
        using var activityListener = CreateListener(parent.TraceId, []);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        List<Measurement<double>> measurements;
        using (var collector = CreateDurationCollector())
        {
            await service.SendPromptAsync(new SessionPromptParams("session-42", []));
            measurements = collector.Snapshot();
        }

        // Assert
        var measurement = Assert.Single(measurements);
        Assert.True(measurement.Value > 0);
        Assert.Equal("TestAgent", TagValue(measurement, ApplicationSemanticConventions.GenAi.AgentName));
        Assert.Null(TagValue(measurement, OtelErrorAttributes.Type));
    }

    [Fact]
    public async Task SendPromptAsync_Cancellation_TagsDurationWithNormalizedCancelType()
    {
        // Arrange：取消要进耗时分布且可分辨（error.type 归一化为 OperationCanceledException），
        // 但不得污染成功分布。
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var acpClient = new Mock<IAcpClient>();
        acpClient.Setup(client => client.AgentInfo)
            .Returns(new AgentInfo("TestAgent", "2.3.4"));
        acpClient
            .Setup(client => client.SendPromptAsync(
                It.IsAny<SessionPromptParams>(),
                cancellation.Token))
            .Returns(Task.FromCanceled<SessionPromptResponse>(cancellation.Token));
        using var parent = new Activity("test.chat.metric.cancelled").Start();
        using var activityListener = CreateListener(parent.TraceId, []);
        using var service = new ChatService(
            acpClient.Object,
            new Mock<IErrorLogger>().Object,
            new SessionManager());

        // Act
        List<Measurement<double>> measurements;
        using (var collector = CreateDurationCollector())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendPromptAsync(
                new SessionPromptParams("session-42", []),
                cancellation.Token));
            measurements = collector.Snapshot();
        }

        // Assert
        var measurement = Assert.Single(measurements);
        Assert.Equal(
            "System.OperationCanceledException",
            TagValue(measurement, OtelErrorAttributes.Type));
    }

    private static string? TagValue(Measurement<double> measurement, string key) =>
        measurement.Tags.ToArray().FirstOrDefault(tag => tag.Key == key).Value as string;

    /// <summary>
    /// 挂一个只收 <c>gen_ai.invoke_agent.duration</c> 的 MeterListener；Dispose 即停。
    /// </summary>
    private sealed class DurationCollector : IDisposable
    {
        private readonly List<Measurement<double>> _measurements = [];
        private readonly MeterListener _listener = new()
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ApplicationMeters.ChatServiceMeterName
                    && instrument.Name == ApplicationSemanticConventions.GenAi.InvokeAgentDurationMetric)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        public DurationCollector()
        {
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            {
                if (instrument.Name == ApplicationSemanticConventions.GenAi.InvokeAgentDurationMetric)
                {
                    _measurements.Add(new Measurement<double>(measurement, tags.ToArray()));
                }
            });
            _listener.Start();
        }

        public List<Measurement<double>> Snapshot() => [.. _measurements];

        public void Dispose() => _listener.Dispose();
    }

    private static DurationCollector CreateDurationCollector() => new();

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

using System;
using System.Diagnostics;
using System.Linq;
using SalmonEgg.Application.Observability;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

/// <summary>
/// 守护 OTel 语义约定合规性。
///
/// 这类断言值得长期保留：字段名写错**不会**导致构建失败或测试变红，只会让后端
/// （Jaeger / Tempo / Honeycomb）静默无法识别异常与堆栈——是典型的“看起来正常、
/// 实际数据不可用”缺陷。故用测试把规范固定值钉死。
/// </summary>
public class SemanticConventionComplianceTests
{
    private const string SourceName = "SemanticConventionComplianceTests";

    /// <summary>
    /// 断言的是**用户可观测的产物**（真实 Activity 上的 event 与 tag 键名），
    /// 而非常量字段本身，因此常量被重命名或绕过时同样会失败。
    /// </summary>
    private static Activity StartRecordedActivity(out ActivityListener listener, out ActivitySource source)
    {
        listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        source = new ActivitySource(SourceName);
        var activity = source.StartActivity("op");
        Assert.NotNull(activity);
        return activity!;
    }

    [Fact]
    public void RecordException_UsesSpecMandatedExceptionEventName()
    {
        using var activity = StartRecordedActivity(out var listener, out var source);
        using (listener)
        using (source)
        {
            activity.RecordException(new InvalidOperationException("boom"));

            // 规范：event 名 MUST 为 "exception"，后端按此名识别异常。
            var ev = Assert.Single(activity.Events);
            Assert.Equal("exception", ev.Name);
        }
    }

    [Fact]
    public void RecordException_UsesExceptionPrefixedAttributeKeys()
    {
        using var activity = StartRecordedActivity(out var listener, out var source);
        using (listener)
        using (source)
        {
            // 必须用**真实抛出**的异常：未抛出的异常 StackTrace 为 null，
            // 实现会按设计跳过 stacktrace 字段，用 new 出来的对象无法覆盖该分支。
            activity.RecordException(CaptureThrown());

            var ev = Assert.Single(activity.Events);
            var keys = ev.Tags.Select(t => t.Key).ToArray();

            // 正向：必须用 exception.* 这组键，否则堆栈在后端 UI 不会被识别。
            Assert.Contains("exception.type", keys);
            Assert.Contains("exception.message", keys);
            Assert.Contains("exception.stacktrace", keys);
        }
    }

    private static Exception CaptureThrown()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void RecordException_DoesNotUseDeprecatedOrNonStandardErrorKeys()
    {
        using var activity = StartRecordedActivity(out var listener, out var source);
        using (listener)
        using (source)
        {
            activity.RecordException(new InvalidOperationException("boom"));

            var ev = Assert.Single(activity.Events);
            var keys = ev.Tags.Select(t => t.Key).ToArray();

            // 反向：error.message 在规范中已弃用，error.stack_trace 非标准键。
            // 曾经的实现用的正是这两个键，此断言防止回归。
            Assert.DoesNotContain("error.message", keys);
            Assert.DoesNotContain("error.stack_trace", keys);
        }
    }

    [Fact]
    public void SetErrorStatus_WritesLowCardinalityErrorType()
    {
        using var activity = StartRecordedActivity(out var listener, out var source);
        using (listener)
        using (source)
        {
            activity.SetErrorStatus(new InvalidOperationException("boom with id 12345"));

            Assert.Equal(ActivityStatusCode.Error, activity.Status);

            // error.type 必须是低基数的类型名，不能是含变量的 message，
            // 否则后端时间序列基数会被打爆。
            var errorType = activity.GetTagItem("error.type") as string;
            Assert.Equal(typeof(InvalidOperationException).FullName, errorType);
            Assert.DoesNotContain("12345", errorType);
        }
    }

    [Fact]
    public void ResourceConventions_UseCurrentNonDeprecatedNames()
    {
        // deployment.environment 已被规范弃用，当前稳定名为 deployment.environment.name。
        Assert.Equal(
            "deployment.environment.name",
            SemanticConventions.Resource.DeploymentEnvironmentName);
    }

    [Fact]
    public void ApplicationPrivateAttributes_AreNamespacedToAvoidCollidingWithStandardKeys()
    {
        // 应用私有键必须带 salmonegg. 前缀，避免占用规范的通用命名空间。
        var applicationKeys = new[]
        {
            ApplicationSemanticConventions.Chat.TransportType,
            ApplicationSemanticConventions.Chat.Command,
            ApplicationSemanticConventions.Chat.Url,
            ApplicationSemanticConventions.Chat.ServiceType,
            ApplicationSemanticConventions.Chat.ProfileId,
        };

        Assert.All(applicationKeys, key => Assert.StartsWith("salmonegg.", key));
    }

    [Fact]
    public void StandardErrorType_IsNotNamespaced()
    {
        // 反向约束：error.type 是规范标准键，不能被误加 salmonegg. 前缀，
        // 否则后端的通用错误聚合会失效。
        Assert.Equal("error.type", OtelErrorAttributes.Type);
    }
}

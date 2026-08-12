using System;
using System.Diagnostics;

namespace SalmonEgg.Application.Observability;

/// <summary>
/// Activity 扩展方法，提供便捷的 OpenTelemetry 操作。
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// 按 OTel 异常约定记录异常。
    ///
    /// 字段名必须是 <c>exception.type</c> / <c>exception.message</c> /
    /// <c>exception.stacktrace</c>，且 event 名必须是 <c>exception</c>：后端
    /// （Jaeger / Tempo / Honeycomb）正是按这组键识别并渲染异常与堆栈。
    /// 不可用 <c>error.message</c> / <c>error.stack_trace</c> 代替——前者在规范中
    /// 已弃用、后者根本不是标准键，写成那样堆栈在 UI 上不会被识别为异常信息。
    /// （<c>error.type</c> 是 span/metric 级别的低基数错误分类属性，与 exception
    /// event 是两套不同约定，勿混用。）
    ///
    /// 参见 https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-spans/
    /// </summary>
    public static Activity? RecordException(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null)
        {
            return activity;
        }

        var tags = new ActivityTagsCollection
        {
            { OtelExceptionAttributes.Type, exception.GetType().FullName },
            { OtelExceptionAttributes.Message, exception.Message }
        };

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            tags.Add(OtelExceptionAttributes.Stacktrace, exception.StackTrace);
        }

        // event 名固定为 "exception"，规范中为 MUST。
        activity.AddEvent(new ActivityEvent(OtelExceptionAttributes.EventName, tags: tags));

        return activity;
    }

    /// <summary>
    /// 标记 span 为错误状态，并按 OTel 约定写入低基数的 <c>error.type</c>。
    ///
    /// 与 <see cref="RecordException"/> 的分工：本方法只描述“失败的类别”，
    /// 供后端做聚合与告警；异常明细（message / stacktrace）由 RecordException
    /// 写进 exception event。两者可以同时使用。
    /// </summary>
    public static Activity? SetErrorStatus(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null)
        {
            return activity;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // error.type 要求低基数：用异常类型全名而非 message，避免 message 中的
        // 变量（ID、路径）把基数打爆。
        activity.SetTag(OtelExceptionAttributes.ErrorType, exception.GetType().FullName);

        return activity;
    }
}

/// <summary>
/// OTel exception event 的键名常量（规范固定值，勿改）。
///
/// 之所以不复用 Infrastructure 层的 <c>SemanticConventions</c>：Infrastructure
/// 引用 Application，Application 反向引用会形成循环依赖。语义约定是纯常量、无任何
/// 依赖，因此由需要它的最低层各自持有；两处必须与规范一致（有测试守护）。
/// </summary>
internal static class OtelExceptionAttributes
{
    /// <summary>规范要求 exception event 的名称 MUST 为 "exception"。</summary>
    public const string EventName = "exception";

    // 反向验证记录：把这三个键改成 error.message / error.stack_trace 会使
    // SemanticConventionComplianceTests 的正向与反向断言同时失败，证明门禁有效。
    public const string Type = "exception.type";
    public const string Message = "exception.message";
    public const string Stacktrace = "exception.stacktrace";

    public const string ErrorType = OtelErrorAttributes.Type;
}

/// <summary>
/// OTel 错误分类属性（span / metric 级）。
///
/// <c>error.type</c> 要求**低基数**：只放异常类型等可枚举的类别，不要放 message、
/// ID、路径等高基数值，否则会打爆后端的时间序列基数。
/// 规范中 <c>error.message</c> 已弃用、<c>error.stack_trace</c> 非标准键，
/// 异常明细一律走 <see cref="OtelExceptionAttributes"/> 的 exception event。
/// </summary>
public static class OtelErrorAttributes
{
    public const string Type = "error.type";
}

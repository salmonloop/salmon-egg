using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry 运行时配置，合并自多个来源（按优先级）：
/// 1. 用户自定义配置（<see cref="Domain.Models.AppSettings.TelemetryCustomEndpoint"/>）
/// 2. 环境变量（OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_SDK_DISABLED）
/// 3. 默认值（<see cref="TelemetryDefaults"/>）
///
/// 用户的 <see cref="Domain.Models.AppSettings.TelemetrySharingEnabled"/> = false 时，
/// 整个 SDK 禁用（等价于 OTEL_SDK_DISABLED=true）。
/// </summary>
public sealed class TelemetrySettings
{
    private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("D");

    /// <summary>
    /// 是否启用 OpenTelemetry SDK。
    /// false 时，所有 tracing/metrics 操作变为 no-op。
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// OTLP exporter 的端点 URL。
    /// 例如：http://localhost:4318 或 https://otel.salmonegg.io:4317
    /// </summary>
    public string? OtlpEndpoint { get; init; }

    /// <summary>
    /// OTLP 协议类型（gRPC 或 HTTP/Protobuf）。
    /// </summary>
    public OtlpProtocol Protocol { get; init; } = OtlpProtocol.HttpProtobuf;

    /// <summary>
    /// OTLP exporter 的认证头（可选）。
    /// 格式：逗号分隔的 <c>key=value</c>，例如 <c>api-key=your-key</c>。
    /// </summary>
    public string? OtlpHeaders { get; init; }

    /// <summary>
    /// 服务名称（OpenTelemetry 资源属性 service.name）。
    /// </summary>
    public string ServiceName { get; init; } = TelemetryDefaults.ServiceName;

    /// <summary>
    /// 服务版本（OpenTelemetry 资源属性 service.version）。
    /// </summary>
    public string? ServiceVersion { get; init; }

    /// <summary>
    /// 自定义 Resource Attributes。
    /// </summary>
    public Dictionary<string, string> ResourceAttributes { get; init; } = new();

    /// <summary>
    /// 差异化采样配置。
    /// </summary>
    public SamplingSettings Sampling { get; init; } = new();

    /// <summary>
    /// 判断两份配置是否会产出同一条导出管线。
    /// </summary>
    /// <remarks>
    /// 用途：app.yaml 有多个写入方且任何设置变更都会落盘，若不比较就重建，改主题、改快捷键
    /// 都会连带拆掉 OTLP 管线并触发一次 flush 等待（最坏 5s），而遥测配置根本没变。
    ///
    /// 刻意做全字段比较而非只比 endpoint/headers：新增字段时若忘记扩充此处，"配置变了却不
    /// 生效"是静默失败，比多一次重建危险得多。因此宁可让新字段默认参与比较。
    /// </remarks>
    public bool IsEquivalentTo(TelemetrySettings? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // 已禁用 ⇒ 无管线可言，其余字段无关；否则改端点再关开关会被判为"不同"而白重建一次。
        if (!Enabled || !other.Enabled)
        {
            return Enabled == other.Enabled;
        }

        return string.Equals(OtlpEndpoint, other.OtlpEndpoint, StringComparison.Ordinal)
            && Protocol == other.Protocol
            && string.Equals(OtlpHeaders, other.OtlpHeaders, StringComparison.Ordinal)
            && string.Equals(ServiceName, other.ServiceName, StringComparison.Ordinal)
            && string.Equals(ServiceVersion, other.ServiceVersion, StringComparison.Ordinal)
            && Sampling.IsEquivalentTo(other.Sampling)
            && AttributesEqual(ResourceAttributes, other.ResourceAttributes);
    }

    private static bool AttributesEqual(
        Dictionary<string, string> left,
        Dictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 容器构建阶段使用的"未激活"配置。
    /// </summary>
    /// <remarks>
    /// 存在意义：DI 工厂里不能读用户设置（异步 IO 同步阻塞会违反启动副作用所有权约束），
    /// 但 <see cref="TelemetryManager"/> 需要一个非 null 的初始配置才能成为真正的单例。
    /// 以"禁用"起步意味着容器构建期间不建任何 provider、不发任何网络请求；真实配置由启动
    /// workflow 异步加载后 apply。
    ///
    /// 不用 <c>Build(null, …)</c> 代替：那会读环境变量并可能判定为启用，于是在真实用户设置
    /// （可能是"已关闭遥测"）加载之前就开始导出——用户的关闭意图会被短暂违反。
    /// </remarks>
    public static TelemetrySettings CreateInactiveBootstrap() => new()
    {
        Enabled = false,
        ServiceName = TelemetryDefaults.ServiceName
    };

    /// <summary>
    /// 从用户设置 + 环境变量 + 默认值构建最终配置。
    /// </summary>
    /// <param name="userSettings">用户在设置界面配置的选项（可为 null，表示未加载）</param>
    /// <param name="platformSamplingDefaults">平台自适应的采样默认值</param>
    /// <param name="serviceVersion">应用版本号</param>
    public static TelemetrySettings Build(
        Domain.Models.AppSettings? userSettings,
        SamplingSettings platformSamplingDefaults,
        string? serviceVersion = null)
    {
        // 配置优先级：用户显式禁用 > 环境变量禁用 > 默认启用
        var envDisabled = Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED");
        var userDisabled = userSettings?.TelemetrySharingEnabled == false;
        var enabled = !userDisabled && envDisabled != "true";

        // 端点优先级：用户自定义 > 环境变量 > 默认值
        var endpoint = userSettings?.TelemetryCustomEndpoint
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? TelemetryDefaults.DefaultOtlpEndpoint;

        // 认证头优先级：用户自定义 > 环境变量
        var headers = userSettings?.TelemetryAuthHeader
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");

        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? TelemetryDefaults.ServiceName;

        var environment = Environment.GetEnvironmentVariable("OTEL_ENVIRONMENT")
            ?? TelemetryDefaults.DefaultEnvironment;

        return new TelemetrySettings
        {
            Enabled = enabled,
            OtlpEndpoint = endpoint,
            Protocol = OtlpProtocol.HttpProtobuf, // 默认 HTTP/Protobuf（兼容性最好）
            OtlpHeaders = headers,
            ServiceName = serviceName,
            ServiceVersion = serviceVersion,
            ResourceAttributes = new Dictionary<string, string>
            {
                // 使用当前稳定规范名称（deployment.environment 已弃用）
                [SemanticConventions.Resource.DeploymentEnvironmentName] = environment,
                // service.instance.id identifies this process lifetime. Reconfiguration must not
                // create a new identity merely because the endpoint or consent changed.
                [SemanticConventions.Resource.ServiceInstanceId] = ProcessInstanceId
            },
            Sampling = platformSamplingDefaults,
        };
    }
}

/// <summary>
/// OTLP 协议类型。
/// </summary>
public enum OtlpProtocol
{
    /// <summary>
    /// gRPC 协议（性能最好，但 WASM 不支持）。
    /// </summary>
    Grpc,

    /// <summary>
    /// HTTP/Protobuf 协议（兼容性最好）。
    /// </summary>
    HttpProtobuf
}

using System;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Platforms.Mobile.Observability;

/// <summary>
/// Mobile 平台（Android/iOS）的 Telemetry 导出器工厂
/// 优化：
/// - 电量敏感：降低采样率
/// - 网络敏感：根据网络状态调整导出策略
/// - 存储受限：不启用文件导出
/// </summary>
public sealed class MobileTelemetryExporterFactory : ITelemetryExporterFactory
{
    public bool IsGrpcSupported => true;  // Android 支持，iOS 视网络库而定
    public bool IsFileSupported => false;  // Mobile 存储受限，不启用文件导出

    public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;  // Mobile 默认不启用本地 console 输出（性能考虑）
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        options.AddOtlpExporter(exporterOptions => ApplyOtlpOptions(exporterOptions, settings));
    }

    /// <summary>
    /// 三个信号维度共用同一份 endpoint / protocol / headers。
    /// </summary>
    /// <remarks>
    /// 抽出来是为了让"漏配 headers"不可能只发生在某一个维度：认证头一旦只加在其中一路，
    /// 另外两路会被后端以 401 静默拒绝，而应用侧看起来"遥测已开启"。
    /// </remarks>
    private void ApplyOtlpOptions(OtlpExporterOptions options, TelemetrySettings settings)
    {
        options.Endpoint = new Uri(settings.OtlpEndpoint!);
        options.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        if (!string.IsNullOrWhiteSpace(settings.OtlpHeaders))
        {
            options.Headers = settings.OtlpHeaders;
        }
    }
}

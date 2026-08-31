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

    public bool IsRuntimeInstrumentationSupported => true;

    public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;  // Mobile 默认不启用本地 console 输出（性能考虑）
        }

        builder.AddOtlpExporter(options => OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Traces,
            IsGrpcSupported));
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Metrics,
            IsGrpcSupported));
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        options.AddOtlpExporter(exporterOptions => OtlpExporterOptionsConfigurator.Apply(
            exporterOptions,
            settings,
            OtlpSignal.Logs,
            IsGrpcSupported));
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Telemetry 生命周期管理实现：构建 Resource、装配 TracerProvider / MeterProvider，
/// 并把平台特定的导出器交由 <see cref="ITelemetryExporterFactory"/> 配置。
/// </summary>
public sealed class TelemetryManager : ITelemetryManager, IDisposable
{
    private readonly TelemetrySettings _settings;
    private readonly ITelemetryExporterFactory _exporterFactory;
    private readonly object _initLock = new();
    private TracerProvider? _tracerProvider;
    private MeterProvider? _meterProvider;
    private bool _initialized;
    private bool _disposed;

    public TelemetryManager(
        TelemetrySettings settings,
        ITelemetryExporterFactory exporterFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _exporterFactory = exporterFactory ?? throw new ArgumentNullException(nameof(exporterFactory));
    }

    public TracerProvider? TracerProvider => _tracerProvider;

    public MeterProvider? MeterProvider => _meterProvider;

    public bool IsEnabled => _settings.Enabled && _initialized;

    public void Initialize()
    {
        lock (_initLock)
        {
            if (_initialized || !_settings.Enabled)
            {
                return;
            }

            try
            {
                var resourceBuilder = BuildResource();

                var tracerBuilder = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(resourceBuilder)
                    .SetSampler(new DifferentialSampler(_settings.Sampling));

                foreach (var sourceName in TelemetrySourceNames.ActivitySources)
                {
                    tracerBuilder.AddSource(sourceName);
                }

                // 顺序有硬要求：提升 processor 必须早于导出 processor 加入 pipeline。
                // SDK 按注册顺序串联 processor，若导出器先注册，它在提升发生前就已
                // 依据 Recorded 位决定跳过该 span，提升将完全无效。
                tracerBuilder.AddProcessor(new ErrorAndLatencyPromotionProcessor(_settings.Sampling));

                _exporterFactory.ConfigureTracerProvider(tracerBuilder, _settings);
                _tracerProvider = tracerBuilder.Build();

                var meterBuilder = Sdk.CreateMeterProviderBuilder()
                    .SetResourceBuilder(resourceBuilder);

                foreach (var meterName in TelemetrySourceNames.Meters)
                {
                    meterBuilder.AddMeter(meterName);
                }

                _exporterFactory.ConfigureMeterProvider(meterBuilder, _settings);
                _meterProvider = meterBuilder.Build();

                _initialized = true;
            }
            catch (Exception ex)
            {
                // 可观测性是旁路能力：初始化失败不得阻断应用启动。
                // 失败后 IsEnabled 保持 false，两个 provider 保持 null。
                Debug.WriteLine($"[SalmonEgg] OpenTelemetry initialization failed: {ex}");
                _tracerProvider?.Dispose();
                _meterProvider?.Dispose();
                _tracerProvider = null;
                _meterProvider = null;
            }
        }
    }

    public bool Shutdown(int timeoutMilliseconds = 5000)
    {
        var tracerOk = _tracerProvider?.Shutdown(timeoutMilliseconds) ?? true;
        var meterOk = _meterProvider?.Shutdown(timeoutMilliseconds) ?? true;
        return tracerOk && meterOk;
    }

    public bool Flush(int timeoutMilliseconds = 5000)
    {
        var tracerOk = _tracerProvider?.ForceFlush(timeoutMilliseconds) ?? true;
        var meterOk = _meterProvider?.ForceFlush(timeoutMilliseconds) ?? true;
        return tracerOk && meterOk;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();

        // ActivitySource / Meter 是进程级静态实例，其生命周期与进程一致，
        // 不在此处 Dispose：TelemetryManager 可以被重建，而释放静态 source 后
        // 任何仍在运行的埋点都会永久失去记录能力。
    }

    private ResourceBuilder BuildResource()
    {
        var builder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: _settings.ServiceName,
                serviceVersion: _settings.ServiceVersion ?? GetAssemblyVersion());

        // 运行时 / 主机维度，键名取自 OTel Semantic Conventions。
        builder.AddAttributes(new Dictionary<string, object>
        {
            [SemanticConventions.Resource.ProcessPid] = (long)Environment.ProcessId,
            [SemanticConventions.Resource.ProcessRuntimeName] = ".NET",
            [SemanticConventions.Resource.ProcessRuntimeVersion] = Environment.Version.ToString(),
            [SemanticConventions.Resource.HostName] = Environment.MachineName,
            [SemanticConventions.Resource.OsType] = GetOsType()
        });

        // 调用方提供的自定义属性放在最后，允许覆盖上面的默认值。
        foreach (var attribute in _settings.ResourceAttributes)
        {
            builder.AddAttributes(new[]
            {
                new KeyValuePair<string, object>(attribute.Key, attribute.Value)
            });
        }

        return builder;
    }

    private static string GetAssemblyVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>
    /// 返回 OTel <c>os.type</c> 约定取值。
    /// 注意顺序：browser-wasm 下 <c>OperatingSystem.IsBrowser()</c> 为 true 的同时
    /// 其他若干判定也可能为 true，故 browser 必须先判断。
    /// </summary>
    private static string GetOsType()
    {
        if (OperatingSystem.IsBrowser()) return "browser";
        if (OperatingSystem.IsAndroid()) return "android";
        if (OperatingSystem.IsIOS()) return "ios";
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "darwin";
        return "unknown";
    }
}

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
    private readonly ITelemetryExporterFactory _exporterFactory;
    private readonly DynamicTelemetryLoggerProvider? _dynamicLoggerProvider;
    private readonly object _initLock = new();
    private TelemetrySettings _settings;
    private TracerProvider? _tracerProvider;
    private MeterProvider? _meterProvider;
    private bool _initialized;
    private bool _disposed;

    /// <param name="settings">
    /// 初始配置。容器构建阶段应传 <see cref="TelemetrySettings.CreateInactiveBootstrap"/>：
    /// 构造函数不装配任何 provider，真实配置由启动流程加载后经 <see cref="Reconfigure"/> 落地。
    /// </param>
    public TelemetryManager(
        TelemetrySettings settings,
        ITelemetryExporterFactory exporterFactory,
        DynamicTelemetryLoggerProvider? dynamicLoggerProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _exporterFactory = exporterFactory ?? throw new ArgumentNullException(nameof(exporterFactory));
        _dynamicLoggerProvider = dynamicLoggerProvider;
    }

    public TracerProvider? TracerProvider => _tracerProvider;

    public MeterProvider? MeterProvider => _meterProvider;

    public bool IsEnabled => _settings.Enabled && _initialized;

    /// <summary>
    /// 按 <see cref="_settings"/> 装配 provider。调用方必须已持有 <see cref="_initLock"/>。
    /// </summary>
    private void BuildProvidersUnderLock()
    {
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

            _dynamicLoggerProvider?.Reconfigure(_settings, resourceBuilder);

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
            _initialized = false;
        }
    }

    /// <summary>
    /// 先 flush 再拆除当前 provider。调用方必须已持有 <see cref="_initLock"/>。
    /// </summary>
    private void TearDownProvidersUnderLock(int flushTimeoutMilliseconds)
    {
        try
        {
            // Shutdown 内部会 flush 并按 timeout 等待导出完成；直接 Dispose 会丢缓冲数据。
            _tracerProvider?.Shutdown(flushTimeoutMilliseconds);
            _meterProvider?.Shutdown(flushTimeoutMilliseconds);
        }
        catch (Exception ex)
        {
            // 导出端不可达时 Shutdown 可能抛错；不得因此阻断后续重建。
            Debug.WriteLine($"[SalmonEgg] OpenTelemetry flush before reconfigure failed: {ex}");
        }

        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
        _tracerProvider = null;
        _meterProvider = null;
        _initialized = false;
    }

    /// <summary>
    /// 用新配置重建遥测管线，使端点/凭证/开关变更立即生效。
    ///
    /// 顺序有硬要求：先 <c>Shutdown</c> 旧 provider（其内部会 flush 并等待导出完成），
    /// 再 Dispose、再用新配置重建。若直接 Dispose 而不 Shutdown，旧 provider 缓冲区里
    /// 尚未导出的 span 会被丢弃——切换端点时丢失的恰恰可能是刚记录的错误 span。
    ///
    /// 静态 ActivitySource / Meter 不受影响：它们与进程同生命周期，重建只换 provider，
    /// 因此重建期间产生的埋点不会崩溃（只是在无 provider 的窗口内不被记录）。
    /// </summary>
    public void Reconfigure(TelemetrySettings newSettings)
    {
        if (newSettings == null)
        {
            throw new ArgumentNullException(nameof(newSettings));
        }

        lock (_initLock)
        {
            if (_disposed)
            {
                return;
            }

            // 先让旧 provider 把缓冲区导完，再拆除。
            TearDownProvidersUnderLock(flushTimeoutMilliseconds: 5000);

            _settings = newSettings;
            _initialized = false;

            // 用户关掉了开关：拆完即止，不再重建。
            if (!newSettings.Enabled)
            {
                _dynamicLoggerProvider?.Reconfigure(newSettings, BuildResource());
                return;
            }

            BuildProvidersUnderLock();
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

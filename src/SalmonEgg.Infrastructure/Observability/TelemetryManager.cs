using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;
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
    private bool _shutdown;
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

    public bool IsEnabled
    {
        get
        {
            lock (_initLock)
            {
                return !_shutdown && !_disposed && _settings.Enabled && _initialized;
            }
        }
    }

    /// <summary>
    /// 按目标配置装配 provider。调用方必须已持有 <see cref="_initLock"/>。
    /// </summary>
    private void BuildProvidersUnderLock(TelemetrySettings targetSettings)
    {
        TracerProvider? candidateTracer = null;
        MeterProvider? candidateMeter = null;
        ILoggerFactory? candidateLoggerFactory = null;
        try
        {
            var resourceBuilder = BuildResource(targetSettings);
            var tracerBuilder = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                // parent-not-sampled 的两个分支必须显式设为 RecordOnly：单参构造会把它们默认成
                // AlwaysOff，而本方案下父 span 常态未 Recorded，子 span 会根本不被创建，
                // error-biased 就只对 root 生效、内层 ACP 请求的错误永久丢失。
                .SetSampler(new ParentBasedSampler(
                    rootSampler: new ErrorBiasedSampler(targetSettings.Sampling.NormalRate),
                    remoteParentSampled: new AlwaysOnSampler(),
                    remoteParentNotSampled: new RecordOnlySampler(),
                    localParentSampled: new AlwaysOnSampler(),
                    localParentNotSampled: new RecordOnlySampler()))
                // 必须在导出器之前注册：处理器按注册顺序成链，导出器读的是被调用那一刻的
                // Recorded flag，晚于它提升就不会被导出（且静默无错）。
                .AddProcessor(new ErrorBiasedExportProcessor());

            foreach (var sourceName in TelemetrySourceNames.ActivitySources)
            {
                tracerBuilder.AddSource(sourceName);
            }

            _exporterFactory.ConfigureTracerProvider(tracerBuilder, targetSettings);
            candidateTracer = tracerBuilder.Build();

            var meterBuilder = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resourceBuilder);
            foreach (var meterName in TelemetrySourceNames.Meters)
            {
                meterBuilder.AddMeter(meterName);
            }

            // 运行时指标（GC / JIT / 线程池 / 工作集）由平台能力位门控：WASM 上调用会抛
            // PlatformNotSupportedException 并使整条管线装配失败。不需要另外 AddMeter——
            // 实测该插装自行注册其 meter（System.Runtime），补一次 AddMeter 是纯冗余。
            if (_exporterFactory.IsRuntimeInstrumentationSupported)
            {
                meterBuilder.AddRuntimeInstrumentation();
            }

            _exporterFactory.ConfigureMeterProvider(meterBuilder, targetSettings);
            candidateMeter = meterBuilder.Build();

            // Construct the logging replacement before retiring the current pipeline. Dynamic
            // provider construction is separate from the swap, so a failure cannot leave a
            // partially updated multi-signal pipeline.
            candidateLoggerFactory = _dynamicLoggerProvider?.BuildReplacement(targetSettings, resourceBuilder);

            if (_dynamicLoggerProvider is not null
                && !_dynamicLoggerProvider.TryCommitReplacement(candidateLoggerFactory))
            {
                throw new ObjectDisposedException(nameof(DynamicTelemetryLoggerProvider));
            }

            var previousTracer = _tracerProvider;
            var previousMeter = _meterProvider;
            _tracerProvider = candidateTracer;
            _meterProvider = candidateMeter;
            _settings = targetSettings;
            candidateTracer = null;
            candidateMeter = null;
            _initialized = true;
            candidateLoggerFactory = null;

            // 新管线已经成为 authoritative runtime。旧 provider 的 flush/dispose 失败只能
            // 影响旧批次，不能把已经完成的切换反向标成失败或拆掉新管线。
            ShutdownAndDispose(previousTracer, 5000);
            ShutdownAndDispose(previousMeter, 5000);
        }
        catch
        {
            DisposeSafely(candidateTracer);
            DisposeSafely(candidateMeter);
            DisposeSafely(candidateLoggerFactory);
            throw;
        }
    }

    /// <summary>
    /// 先 flush 再拆除当前 provider。调用方必须已持有 <see cref="_initLock"/>。
    /// </summary>
    private void TearDownProvidersUnderLock(int flushTimeoutMilliseconds)
    {
        var previousTracer = _tracerProvider;
        var previousMeter = _meterProvider;
        _tracerProvider = null;
        _meterProvider = null;
        _initialized = false;

        ShutdownAndDispose(previousTracer, flushTimeoutMilliseconds);
        ShutdownAndDispose(previousMeter, flushTimeoutMilliseconds);
    }

    /// <summary>
    /// 用新配置重建遥测管线，使端点/凭证/开关变更立即生效。
    ///
    /// 顺序有硬要求：先完整构造候选管线，成功后原子替换，再 <c>Shutdown</c> / Dispose
    /// 旧 provider。这样候选构造失败时旧管线仍可用，成功切换时也不会出现无 provider 窗口。
    ///
    /// 静态 ActivitySource / Meter 不受影响：它们与进程同生命周期，重建只换 provider。
    /// </summary>
    public void Reconfigure(TelemetrySettings newSettings)
    {
        if (newSettings == null)
        {
            throw new ArgumentNullException(nameof(newSettings));
        }

        lock (_initLock)
        {
            if (_disposed || _shutdown)
            {
                return;
            }

            if (_initialized && newSettings.IsEquivalentTo(_settings))
            {
                return;
            }

            if (!newSettings.Enabled)
            {
                TearDownProvidersUnderLock(flushTimeoutMilliseconds: 5000);
                _dynamicLoggerProvider?.Reconfigure(newSettings, BuildResource(newSettings));
                _settings = newSettings;
                return;
            }

            BuildProvidersUnderLock(newSettings);
        }
    }

    public bool Shutdown(int timeoutMilliseconds = 5000)
    {
        lock (_initLock)
        {
            if (_disposed)
            {
                return true;
            }

            _shutdown = true;
            var tracerOk = ShutdownAndDispose(_tracerProvider, timeoutMilliseconds);
            var meterOk = ShutdownAndDispose(_meterProvider, timeoutMilliseconds);
            _tracerProvider = null;
            _meterProvider = null;
            _initialized = false;

            // 不走 Reconfigure：那条路会同步 Dispose 退役的 OTel logger factory，而
            // LoggerProviderSdk.Dispose 硬编码 Processor.Shutdown(5000)，会成为第三段
            // 不受 timeoutMilliseconds 约束的等待（issue #126）。摘除同步完成，释放交后台。
            _dynamicLoggerProvider?.RetireWithoutWaitingForExport();
            return tracerOk && meterOk;
        }
    }

    public bool Flush(int timeoutMilliseconds = 5000)
    {
        lock (_initLock)
        {
            if (_disposed || _shutdown)
            {
                return true;
            }

            var tracerOk = ForceFlushSafely(_tracerProvider, timeoutMilliseconds);
            var meterOk = ForceFlushSafely(_meterProvider, timeoutMilliseconds);
            return tracerOk && meterOk;
        }
    }

    public void Dispose()
    {
        lock (_initLock)
        {
            if (_disposed)
            {
                return;
            }

            _shutdown = true;
            _disposed = true;
            ShutdownAndDispose(_tracerProvider, 5000);
            ShutdownAndDispose(_meterProvider, 5000);
            _tracerProvider = null;
            _meterProvider = null;
            _initialized = false;
            _dynamicLoggerProvider?.Dispose();
        }
    }

    private static bool ShutdownAndDispose(TracerProvider? provider, int timeoutMilliseconds)
        => ShutdownAndDispose(provider, timeoutMilliseconds, static (candidate, timeout) => candidate.Shutdown(timeout));

    private static bool ShutdownAndDispose(MeterProvider? provider, int timeoutMilliseconds)
        => ShutdownAndDispose(provider, timeoutMilliseconds, static (candidate, timeout) => candidate.Shutdown(timeout));

    private static bool ShutdownAndDispose<TProvider>(
        TProvider? provider,
        int timeoutMilliseconds,
        Func<TProvider, int, bool> shutdown)
        where TProvider : BaseProvider
    {
        if (provider is null)
        {
            return true;
        }

        var shutdownSucceeded = false;
        try
        {
            shutdownSucceeded = shutdown(provider, timeoutMilliseconds);
        }
        catch
        {
            // Exporter teardown is best effort. The provider is still disposed below so one failed
            // signal cannot retain resources or prevent the remaining signals from shutting down.
        }

        DisposeSafely(provider);
        return shutdownSucceeded;
    }

    private static bool ForceFlushSafely(TracerProvider? provider, int timeoutMilliseconds)
        => ForceFlushSafely(provider, timeoutMilliseconds, static (candidate, timeout) => candidate.ForceFlush(timeout));

    private static bool ForceFlushSafely(MeterProvider? provider, int timeoutMilliseconds)
        => ForceFlushSafely(provider, timeoutMilliseconds, static (candidate, timeout) => candidate.ForceFlush(timeout));

    private static bool ForceFlushSafely<TProvider>(
        TProvider? provider,
        int timeoutMilliseconds,
        Func<TProvider, int, bool> forceFlush)
        where TProvider : BaseProvider
    {
        if (provider is null)
        {
            return true;
        }

        try
        {
            return forceFlush(provider, timeoutMilliseconds);
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeSafely(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // Cleanup must not replace the authoritative reconfiguration result with an exception.
        }
    }

    private ResourceBuilder BuildResource(TelemetrySettings settings)
    {
        var builder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: settings.ServiceName,
                serviceVersion: settings.ServiceVersion ?? GetAssemblyVersion());

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
        foreach (var attribute in settings.ResourceAttributes)
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

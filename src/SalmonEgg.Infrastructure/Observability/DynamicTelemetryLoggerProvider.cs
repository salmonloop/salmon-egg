using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 稳定的 DI logger provider，其内部的 OpenTelemetry logger factory 可在遥测设置落盘后被替换。
/// </summary>
/// <remarks>
/// 为什么需要这一层：OTel 的 <c>LoggerProvider</c> 单独 build 出来并不参与
/// <c>Microsoft.Extensions.Logging</c> 的分发，业务代码的 <c>ILogger</c> 写入根本不会进入它，
/// 会造成"provider 建好了但 Logs 维度没有数据"的假集成。唯一能收到业务日志的接线方式是
/// <c>ILoggingBuilder.AddOpenTelemetry</c>；而它构造出的 factory 无法原地改配置，所以这里持有
/// 一个可替换的内部 factory，让端点 / 凭证变更后日志与 traces / metrics 一起切换。
///
/// 只有本 Infrastructure 适配器依赖 OpenTelemetry 类型，应用与 Domain 层的日志抽象不受污染。
/// </remarks>
public sealed class DynamicTelemetryLoggerProvider : ILoggerProvider
{
    private readonly ITelemetryExporterFactory _exporterFactory;
    private readonly object _sync = new();

    // 每个 category 的外壳 logger 长期存活（MEL 会长期持有它），内部实现随重配置切换。
    private readonly ConcurrentDictionary<string, DynamicTelemetryLogger> _loggers = new(StringComparer.Ordinal);

    private ILoggerFactory? _innerFactory;

    // 代次号：外壳 logger 据此判断缓存的内部 logger 是否已过期。没有它的话每条日志都要进锁
    // 重建 logger（日志是热路径，IsEnabled + Log 各一次，等于每行日志多次加锁与分配）。
    private int _generation;

    private bool _disposed;

    public DynamicTelemetryLoggerProvider(ITelemetryExporterFactory exporterFactory)
    {
        _exporterFactory = exporterFactory ?? throw new ArgumentNullException(nameof(exporterFactory));
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(
            categoryName ?? string.Empty,
            static (name, owner) => new DynamicTelemetryLogger(owner, name),
            this);

    /// <summary>
    /// 按新配置重建内部 OTel logger factory；<c>Enabled=false</c> 时清空（日志不再上报）。
    /// </summary>
    public void Reconfigure(TelemetrySettings settings, ResourceBuilder resourceBuilder)
    {
        var replacement = BuildReplacement(settings, resourceBuilder);
        if (!TryCommitReplacement(replacement))
        {
            DisposeSafely(replacement);
        }
    }

    internal ILoggerFactory? BuildReplacement(TelemetrySettings settings, ResourceBuilder resourceBuilder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceBuilder);

        ILoggerFactory? replacement = null;
        if (settings.Enabled)
        {
            // 在锁外构造：AddOpenTelemetry 会创建 exporter 与 HttpClient，持锁执行会让并发
            // 写日志的线程一起卡在 IsEnabled 上。
            replacement = LoggerFactory.Create(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Information);
                loggingBuilder.AddOpenTelemetry(options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.SetResourceBuilder(resourceBuilder);
                    _exporterFactory.ConfigureLoggerProvider(options, settings);
                });
            });
        }

        return replacement;
    }

    internal bool TryCommitReplacement(ILoggerFactory? replacement)
    {
        ILoggerFactory? previous;
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            previous = _innerFactory;
            _innerFactory = replacement;

            // 必须在替换之后自增：外壳 logger 看到代次变化才会丢弃指向旧 factory 的缓存。
            _generation++;
        }

        // 在锁外 Dispose：它会 flush 并等待旧批次导出完成，持锁会把写日志的线程一并卡住。
        DisposeSafely(previous);
        return true;
    }

    private (ILogger? Logger, int Generation) ResolveInnerLogger(string categoryName)
    {
        lock (_sync)
        {
            return (_innerFactory?.CreateLogger(categoryName), _generation);
        }
    }

    private int CurrentGeneration => Volatile.Read(ref _generation);

    public void Dispose()
    {
        ILoggerFactory? previous;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            previous = _innerFactory;
            _innerFactory = null;
            _generation++;
        }

        DisposeSafely(previous);
    }

    private static void DisposeSafely(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // The replacement is already authoritative. Cleanup failure in the retired logger
            // factory must not roll the runtime back to a pipeline that no longer owns writes.
        }
    }

    /// <summary>
    /// 面向 MEL 的稳定外壳：生命周期与本 provider 一致，内部实现随配置切换。
    /// </summary>
    private sealed class DynamicTelemetryLogger : ILogger
    {
        private readonly DynamicTelemetryLoggerProvider _owner;
        private readonly string _categoryName;
        private ILogger? _cached;
        private int _cachedGeneration = -1;

        public DynamicTelemetryLogger(DynamicTelemetryLoggerProvider owner, string categoryName)
        {
            _owner = owner;
            _categoryName = categoryName;
        }

        private ILogger? Current
        {
            get
            {
                // 无锁快路径：绝大多数日志写入命中这里。
                if (Volatile.Read(ref _cachedGeneration) == _owner.CurrentGeneration)
                {
                    return Volatile.Read(ref _cached);
                }

                var (logger, generation) = _owner.ResolveInnerLogger(_categoryName);

                // 先写 logger 再写代次：反序会让另一线程看到"代次已最新但 logger 还是旧的"。
                Volatile.Write(ref _cached, logger);
                Volatile.Write(ref _cachedGeneration, generation);
                return logger;
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => Current?.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => Current?.IsEnabled(logLevel) == true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Current?.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}

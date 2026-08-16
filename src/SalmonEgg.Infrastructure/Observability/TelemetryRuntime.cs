using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// <see cref="ITelemetryRuntime"/> 的实现：把已持久化的设置快照投影到 <see cref="ITelemetryManager"/>。
/// </summary>
/// <remarks>
/// 不在此处做输入防抖：唯一的防抖 owner 是设置页的保存链路（已有 750ms），且本服务的触发点
/// 是"已落盘"事件而非按键。再加一层 debounce 会产生第二个 owner，并让"保存成功但遥测还没切"
/// 的窗口无上限地变长。
/// </remarks>
public sealed class TelemetryRuntime : ITelemetryRuntime
{
    private readonly ITelemetryManager _telemetryManager;
    private readonly Func<SamplingSettings> _samplingDefaultsProvider;
    private readonly ILogger<TelemetryRuntime> _logger;
    private readonly string? _serviceVersion;

    // 串行化 apply：provider 重建不可并发，否则两次重建会互相拆掉对方刚建好的 provider。
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    // 与 ConfigProjectionReloadCoordinator 同型的 latest-intent 判定：等锁期间被更新的
    // 快照取代时直接退出，避免"先到的慢 apply 后落地"把运行态压回旧配置。
    private long _applyVersion;

    private TelemetrySettings? _appliedSettings;

    public TelemetryRuntime(
        ITelemetryManager telemetryManager,
        Func<SamplingSettings> samplingDefaultsProvider,
        ILogger<TelemetryRuntime> logger,
        string? serviceVersion = null)
    {
        _telemetryManager = telemetryManager ?? throw new ArgumentNullException(nameof(telemetryManager));
        _samplingDefaultsProvider = samplingDefaultsProvider ?? throw new ArgumentNullException(nameof(samplingDefaultsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceVersion = serviceVersion;
    }

    public async Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var version = Interlocked.Increment(ref _applyVersion);

        try
        {
            var target = TelemetrySettings.Build(
                settings,
                _samplingDefaultsProvider(),
                _serviceVersion);

            await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 已被更新的 apply 取代：本次结果一定会被立刻覆盖，重建纯属浪费且会多一次 flush。
                if (Volatile.Read(ref _applyVersion) != version)
                {
                    return;
                }

                if (target.IsEquivalentTo(_appliedSettings))
                {
                    return;
                }

                // 同步阻塞（内部按 timeout 等待旧批次导出完成）：移出调用线程，
                // 否则设置页保存链路或关闭流程会被压在 UI/调用线程上数秒。
                await Task.Run(() => _telemetryManager.Reconfigure(target), cancellationToken)
                    .ConfigureAwait(false);

                _appliedSettings = target;

                _logger.LogInformation(
                    "Telemetry pipeline applied. enabled={Enabled} endpointHost={EndpointHost} hasHeaders={HasHeaders}",
                    target.Enabled,
                    GetEndpointHost(target.OtlpEndpoint),
                    !string.IsNullOrWhiteSpace(target.OtlpHeaders));
            }
            finally
            {
                _applyGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方取消：正常路径，不记为错误。
        }
        catch (Exception ex)
        {
            // 遥测是旁路能力：重建失败只应停用遥测，不得让设置保存或应用启动失败。
            _logger.LogError(ex, "Failed to apply telemetry configuration; telemetry may be inactive");
        }
    }

    private static string? GetEndpointHost(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 同上：Shutdown 同步阻塞等待导出，不能占用关闭流程所在线程。
            await Task.Run(() => _telemetryManager.Shutdown(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // 关闭路径不得抛：flush 失败不应阻塞进程退出。
            _logger.LogError(ex, "Telemetry shutdown failed; buffered telemetry may be lost");
        }
    }
}

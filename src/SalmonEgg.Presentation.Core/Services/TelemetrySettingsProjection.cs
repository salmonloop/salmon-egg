using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// 把「设置已落盘」这一事实投影到遥测运行态，使端点 / 凭证 / 开关变更立即生效，无需重启。
/// </summary>
/// <remarks>
/// 触发点选择：订阅 <see cref="IAppSettingsService.Saved"/> 而不是 ViewModel 的
/// <c>PropertyChanged</c>，也不是在保存链路里追加一次调用。
///
/// - 不用 PropertyChanged：那是逐次按键的意图流，会让用户每敲一个字符就重建一次 OTLP 管线
///   （每次都要等旧批次 flush）；而且未落盘的意图可能因保存失败而回滚，运行态会与磁盘不一致。
/// - 不在保存链路里追加调用：app.yaml 有多个写入方（设置页保存、云配置恢复），逐个接线必然
///   漏掉其中一条，形成第二套 owner。订阅持久化边界则天然覆盖全部写入方，且事件在写入互斥区
///   内按落盘顺序触发，无需自行识别乱序。
///
/// 无关设置（主题、快捷键）同样会触发本投影，由 <see cref="ITelemetryRuntime.ApplyAsync"/>
/// 的幂等判定负责短路，此处不再重复判断"哪些字段算遥测字段"——那会形成第二份判定标准。
/// </remarks>
public sealed class TelemetrySettingsProjection : IDisposable
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly ITelemetryRuntime _telemetryRuntime;
    private readonly ILogger<TelemetrySettingsProjection> _logger;
    private bool _disposed;

    public TelemetrySettingsProjection(
        IAppSettingsService appSettingsService,
        ITelemetryRuntime telemetryRuntime,
        ILogger<TelemetrySettingsProjection> logger)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _telemetryRuntime = telemetryRuntime ?? throw new ArgumentNullException(nameof(telemetryRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _appSettingsService.Saved += OnSettingsSaved;
    }

    private void OnSettingsSaved(object? sender, AppSettingsSavedEventArgs args)
    {
        // 事件在持久化的写入互斥区内触发：不能在这里 await，否则遥测重建会把 app.yaml 的写锁
        // 一直按住，任何后续保存都要排在一次 provider 重建（含 flush 等待）之后。
        _ = ApplyAsync(args.Settings);
    }

    private async Task ApplyAsync(Domain.Models.AppSettings settings)
    {
        try
        {
            await _telemetryRuntime.ApplyAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ApplyAsync 契约上不抛；这里是 fire-and-forget 的最后一道防线，
            // 未观测的异常会变成 UnobservedTaskException 而不是可诊断的日志。
            _logger.LogError(ex, "Failed to project persisted settings onto the telemetry runtime");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appSettingsService.Saved -= OnSettingsSaved;
    }
}

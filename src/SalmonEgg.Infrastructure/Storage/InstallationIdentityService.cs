using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// <see cref="IInstallationIdentityService"/> 的实现：把安装标识持久化在 app data 根目录下的
/// 独立文件里。
/// </summary>
/// <remarks>
/// 落点选择是硬约束，不是风格问题：<see cref="IAppDataService.ConfigRootPath"/>
/// （即 <c>&lt;appdata&gt;/config</c>）整棵子树会被云配置同步用
/// <c>EnumerateFiles(ConfigRootPath, "*", AllDirectories)</c> 通配打包。装机标识一旦落在
/// 那里，用户开启同步后第二台设备会恢复出同一个值，两台机器从此上报相同标识——装机数与
/// DAU 永久偏低，且数据侧完全看不出异常（无报错、无缺口）。因此本文件放在 app data **根**
/// 目录下，与 <c>config-migrations</c> 同级，天然在同步范围之外。
///
/// 不复用 <c>AppSettings</c> 同理：那是 app.yaml 的内容，属于可携带配置。
///
/// 采用 <see cref="IAppFileStore"/> 而非直接 <c>File.*</c>：WASM 的存储后端不是本地文件系统，
/// 该抽象已承担平台差异。
/// </remarks>
public sealed class InstallationIdentityService : IInstallationIdentityService
{
    /// <summary>文件名。放 app data 根目录，不进 config 子树（会被云同步打包）。</summary>
    internal const string FileName = "installation-id";

    private readonly IAppFileStore _fileStore;
    private readonly IAppDataService _appData;
    private readonly ILogger<InstallationIdentityService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public InstallationIdentityService(
        IAppFileStore fileStore,
        IAppDataService appData,
        ILogger<InstallationIdentityService> logger)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null)
        {
            return cached;
        }

        // 串行化首次生成：并发首启会各生成一个 GUID，后写者覆盖前者，于是同一台设备在同一次
        // 启动内先后上报两个不同标识，装机数偏高。
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref _cached);
            if (cached is not null)
            {
                return cached;
            }

            var path = GetIdentityFilePath();
            var existing = await ReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                Volatile.Write(ref _cached, existing);
                return existing;
            }

            // 规范禁止用硬件 ID（序列号 / IMEI / MAC）派生，故只能是随机值。
            var generated = Guid.NewGuid().ToString("D");
            if (!await TryPersistAsync(path, generated, cancellationToken).ConfigureAwait(false))
            {
                // 持久化失败时不缓存：下次启动仍会重试，避免把一个只存在于内存的值当成
                // 「本次安装的稳定标识」上报（那会让每次启动都算一台新设备）。
                return null;
            }

            Volatile.Write(ref _cached, generated);
            return generated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetIdentityFilePath()
        => Path.Combine(_appData.AppDataRootPath, FileName);

    private async Task<string?> ReadExistingAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _fileStore.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            // 只接受规范形态的 GUID：文件被外部改坏时重新生成，而不是把任意字符串当标识
            // 上报（否则会污染后端的维度取值）。
            return Guid.TryParse(content.Trim(), out var parsed)
                ? parsed.ToString("D")
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read installation identity; a new value will be generated");
            return null;
        }
    }

    private async Task<bool> TryPersistAsync(string path, string value, CancellationToken cancellationToken)
    {
        try
        {
            await _fileStore.WriteAllTextAsync(path, value, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 遥测是旁路能力：写不进去只应让本次会话没有装机标识，不得让启动失败。
            _logger.LogWarning(ex, "Failed to persist installation identity; telemetry will omit it this session");
            return false;
        }
    }
}

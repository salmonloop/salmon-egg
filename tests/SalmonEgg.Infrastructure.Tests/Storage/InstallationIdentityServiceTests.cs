using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

/// <summary>
/// 装机标识的行为门禁。
/// </summary>
/// <remarks>
/// 最重要的一条是落点：标识文件必须**不在** <see cref="IAppDataService.ConfigRootPath"/>
/// 子树内，因为云配置同步用 <c>EnumerateFiles(ConfigRootPath, "*", AllDirectories)</c> 通配
/// 打包该子树。落进去会让第二台设备恢复出同一标识，装机数与 DAU 永久偏低且数据侧无异常可见。
/// 该缺陷不会有任何运行时症状，只能靠这条断言拦住。
/// </remarks>
public sealed class InstallationIdentityServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_PersistsOutsideCloudSyncedConfigSubtree()
    {
        var store = new RecordingFileStore();
        var appData = new FakeAppDataService();
        var service = CreateService(store, appData);

        var id = await service.GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(id);
        var writtenPath = Assert.Single(store.Writes.Keys);

        // config 子树整棵会被云同步打包；标识必须在其外。
        var configPrefix = appData.ConfigRootPath + Path.DirectorySeparatorChar;
        Assert.DoesNotContain(configPrefix, writtenPath, StringComparison.Ordinal);
        Assert.NotEqual(appData.ConfigRootPath, Path.GetDirectoryName(writtenPath));

        // 而且必须确实在 app data 根下，否则只是"碰巧不在 config 里"。
        Assert.Equal(appData.AppDataRootPath, Path.GetDirectoryName(writtenPath));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsStableValueAcrossCalls()
    {
        var store = new RecordingFileStore();
        var service = CreateService(store, new FakeAppDataService());

        var first = await service.GetOrCreateAsync(TestContext.Current.CancellationToken);
        var second = await service.GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        // 第二次不得重新写盘，否则每次调用都会换值。
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesPersistedValue_SimulatingRelaunch()
    {
        var store = new RecordingFileStore();
        var appData = new FakeAppDataService();

        var firstRun = await CreateService(store, appData).GetOrCreateAsync(TestContext.Current.CancellationToken);

        // 新实例 + 同一存储 = 重启：规范要求跨启动（含应用升级）保持同一值。
        var secondRun = await CreateService(store, appData).GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(firstRun, secondRun);
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task GetOrCreateAsync_ProducesRandomGuid_NotDerivedFromHardwareOrMachineIdentity()
    {
        var appData = new FakeAppDataService();

        var first = await CreateService(new RecordingFileStore(), appData)
            .GetOrCreateAsync(TestContext.Current.CancellationToken);
        var second = await CreateService(new RecordingFileStore(), appData)
            .GetOrCreateAsync(TestContext.Current.CancellationToken);

        // 规范：硬件 ID（序列号 / IMEI / MAC）MUST NOT 用作该值。两个空存储上生成的值必须
        // 不同——若实现改成从机器名 / MAC 派生，同一台机器上会得到相同值，此断言即失败。
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParse(first, out _));

        // 也不得包含本机可识别信息。
        Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrCreateAsync_RegeneratesWhenPersistedValueIsCorrupted()
    {
        var appData = new FakeAppDataService();
        var store = new RecordingFileStore();
        store.Seed(Path.Combine(appData.AppDataRootPath, InstallationIdentityService.FileName), "not-a-guid");

        var id = await CreateService(store, appData).GetOrCreateAsync(TestContext.Current.CancellationToken);

        // 不把任意字符串当标识上报（会污染后端维度取值）。
        Assert.NotNull(id);
        Assert.True(Guid.TryParse(id, out _));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsNullWithoutThrowing_WhenPersistenceFails()
    {
        var store = new RecordingFileStore { FailWrites = true };
        var service = CreateService(store, new FakeAppDataService());

        // 遥测是旁路能力：写不进去只应让本次会话没有装机标识，不得让启动流程失败。
        var id = await service.GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.Null(id);
    }

    [Fact]
    public async Task GetOrCreateAsync_DoesNotCacheUnpersistedValue()
    {
        var store = new RecordingFileStore { FailWrites = true };
        var service = CreateService(store, new FakeAppDataService());

        Assert.Null(await service.GetOrCreateAsync(TestContext.Current.CancellationToken));

        // 恢复写入能力后应重试并成功：缓存一个只存在于内存的值会让每次启动都算一台新设备。
        store.FailWrites = false;
        var recovered = await service.GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(recovered);
        Assert.True(Guid.TryParse(recovered, out _));
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentFirstLaunch_YieldsSingleIdentity()
    {
        var store = new RecordingFileStore();
        var service = CreateService(store, new FakeAppDataService());

        // 并发首启若各自生成再互相覆盖，同一台设备会在一次启动内上报两个不同标识。
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                service.GetOrCreateAsync(TestContext.Current.CancellationToken)));

        Assert.Single(results.Distinct(StringComparer.Ordinal));
        Assert.Single(store.Writes);
    }

    private static InstallationIdentityService CreateService(
        RecordingFileStore store,
        FakeAppDataService appData)
        => new(store, appData, NullLogger<InstallationIdentityService>.Instance);

    private sealed class FakeAppDataService : IAppDataService
    {
        public string AppDataRootPath { get; } =
            Path.Combine(Path.GetTempPath(), "salmonegg-install-id-tests", Guid.NewGuid().ToString("N"));

        public string ConfigRootPath => Path.Combine(AppDataRootPath, "config");

        public string LogsDirectoryPath => Path.Combine(AppDataRootPath, "logs");

        public string CacheRootPath => Path.Combine(AppDataRootPath, "cache");

        public string ExportsDirectoryPath => Path.Combine(AppDataRootPath, "exports");
    }

    /// <summary>内存文件存储：不触真实磁盘，且能记录写入路径供落点断言。</summary>
    private sealed class RecordingFileStore : IAppFileStore
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
        private readonly Lock _sync = new();

        public bool FailWrites { get; set; }

        public Dictionary<string, string> Writes
        {
            get
            {
                lock (_sync)
                {
                    return new Dictionary<string, string>(_files, StringComparer.Ordinal);
                }
            }
        }

        public void Seed(string path, string content)
        {
            lock (_sync)
            {
                _files[path] = content;
            }
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_files.ContainsKey(path));
            }
        }

        public Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_files.TryGetValue(path, out var content) ? content : null);
            }
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new IOException("simulated persistence failure");
            }

            lock (_sync)
            {
                _files[path] = content;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _files.Remove(path);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> EnumerateFilesAsync(
            string directory,
            string searchPattern,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            List<string> snapshot;
            lock (_sync)
            {
                snapshot = _files.Keys.Where(key => key.StartsWith(directory, StringComparison.Ordinal)).ToList();
            }

            foreach (var path in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}

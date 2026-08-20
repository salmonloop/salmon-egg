using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class TelemetrySettingsProjectionTests
{
    [Fact]
    public async Task WhenSettingsPersisted_AppliesThatSnapshotToTelemetryRuntime()
    {
        // 这是"保存后立即生效"的行为契约：落盘即重建，不等重启。
        var settings = new AppSettings
        {
            TelemetryCustomEndpoint = "https://collector.example.com:4318",
            TelemetryAuthHeader = "api-key=abc123"
        };
        var telemetry = new RecordingTelemetryRuntime();
        var settingsService = new FakeAppSettingsService();
        using var projection = CreateProjection(settingsService, telemetry);

        await settingsService.SaveAsync(settings);

        var applied = await telemetry.WaitForNextApplyAsync();
        Assert.Same(settings, applied);
    }

    [Fact]
    public async Task WhenSettingsPersistedByAnotherWriter_StillApplies()
    {
        // 云配置恢复也写 app.yaml。订阅持久化边界的意义就在这里：不需要为每个写入方单独接线，
        // 否则这条路径会静默漏掉，运行态一直指向旧端点。
        var telemetry = new RecordingTelemetryRuntime();
        var settingsService = new FakeAppSettingsService();
        using var projection = CreateProjection(settingsService, telemetry);

        // 模拟非设置页写入方（CloudConfigSyncCoordinator 走的是同一个 SaveAsync）。
        var restored = new AppSettings { TelemetrySharingEnabled = false };
        await settingsService.SaveAsync(restored);

        var applied = await telemetry.WaitForNextApplyAsync();
        Assert.Same(restored, applied);
    }

    [Fact]
    public async Task AfterDispose_StopsApplying()
    {
        var telemetry = new RecordingTelemetryRuntime();
        var settingsService = new FakeAppSettingsService();
        var projection = CreateProjection(settingsService, telemetry);

        await settingsService.SaveAsync(new AppSettings());
        await telemetry.WaitForNextApplyAsync();

        projection.Dispose();
        await settingsService.SaveAsync(new AppSettings { TelemetrySharingEnabled = false });

        // 反向验证记录：移除 Dispose 中的取消订阅后，本断言会因收到第 2 次 apply 而失败。
        Assert.False(
            await telemetry.SawAnotherApplyWithinAsync(TimeSpan.FromMilliseconds(300)),
            "Disposed projection must not keep applying persisted settings.");
    }

    [Fact]
    public async Task WhenTelemetryApplyThrows_SaveStillSucceeds()
    {
        // 遥测是旁路能力：重建失败不得让"设置已保存"变成失败——磁盘已经写好了。
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ApplyAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("exporter build failed"));
        var settingsService = new FakeAppSettingsService();
        using var projection = CreateProjection(settingsService, telemetry.Object);

        var exception = await Record.ExceptionAsync(() => settingsService.SaveAsync(new AppSettings()));

        Assert.Null(exception);
    }

    private static TelemetrySettingsProjection CreateProjection(
        IAppSettingsService settingsService,
        ITelemetryRuntime telemetryRuntime)
        => new(settingsService, telemetryRuntime, NullLogger<TelemetrySettingsProjection>.Instance);

    private sealed class RecordingTelemetryRuntime : ITelemetryRuntime
    {
        private readonly SemaphoreSlim _applied = new(0);
        private readonly List<AppSettings> _snapshots = new();

        public Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            lock (_snapshots)
            {
                _snapshots.Add(settings);
            }

            _applied.Release();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<AppSettings> WaitForNextApplyAsync()
        {
            Assert.True(
                await _applied.WaitAsync(TimeSpan.FromSeconds(5)),
                "Telemetry runtime was never asked to apply the persisted settings.");

            lock (_snapshots)
            {
                return _snapshots[^1];
            }
        }

        public Task<bool> SawAnotherApplyWithinAsync(TimeSpan timeout) => _applied.WaitAsync(timeout);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        private AppSettings _settings = new();

        public event EventHandler<AppSettingsSavedEventArgs>? Saved;

        public Task<AppSettings> LoadAsync() => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings)
        {
            _settings = settings;
            Saved?.Invoke(this, new AppSettingsSavedEventArgs(settings));
            return Task.CompletedTask;
        }
    }
}

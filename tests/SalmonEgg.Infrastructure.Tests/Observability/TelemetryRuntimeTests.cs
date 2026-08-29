using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public sealed class TelemetryRuntimeTests
{
    [Fact]
    public async Task ApplyAsync_BuildsPipelineFromPersistedSettings()
    {
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings
        {
            TelemetrySharingEnabled = true,
            TelemetryCustomEndpoint = "https://collector.example.com:4318",
            TelemetryAuthHeader = "api-key=abc123"
        });

        var applied = Assert.Single(manager.Applied);
        Assert.True(applied.Enabled);
        Assert.Equal("https://collector.example.com:4318", applied.OtlpEndpoint);
        Assert.Equal("api-key=abc123", applied.OtlpHeaders);
    }

    [Fact]
    public async Task ApplyAsync_WhenTelemetryDisabled_TearsDownWithoutRebuilding()
    {
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings { TelemetrySharingEnabled = false });

        var applied = Assert.Single(manager.Applied);
        Assert.False(applied.Enabled);
    }

    [Fact]
    public async Task ApplyAsync_WhenEndpointChanges_RebuildsPipeline()
    {
        // 这是"改端点立即生效"的核心断言。
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://first.example.com:4318" });
        await runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://second.example.com:4318" });

        Assert.Equal(2, manager.Applied.Count);
        Assert.Equal("https://second.example.com:4318", manager.Applied[^1].OtlpEndpoint);
    }

    [Fact]
    public async Task ApplyAsync_WhenOnlyCredentialsChange_RebuildsPipeline()
    {
        // 凭证也必须立即生效：只换 key 而不重建，导出会继续用旧凭证被拒。
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings
        {
            TelemetryCustomEndpoint = "https://collector.example.com:4318",
            TelemetryAuthHeader = "api-key=old"
        });
        await runtime.ApplyAsync(new AppSettings
        {
            TelemetryCustomEndpoint = "https://collector.example.com:4318",
            TelemetryAuthHeader = "api-key=new"
        });

        Assert.Equal(2, manager.Applied.Count);
        Assert.Equal("api-key=new", manager.Applied[^1].OtlpHeaders);
    }

    [Fact]
    public async Task ApplyAsync_WhenUnrelatedSettingChanges_DoesNotRebuildPipeline()
    {
        // app.yaml 的任何写入都会触发投影：改主题不得连带拆掉 OTLP 管线并等一次 flush。
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings
        {
            Theme = "Light",
            TelemetryCustomEndpoint = "https://collector.example.com:4318"
        });
        await runtime.ApplyAsync(new AppSettings
        {
            Theme = "Dark",
            TelemetryCustomEndpoint = "https://collector.example.com:4318"
        });

        Assert.Single(manager.Applied);
    }

    [Fact]
    public async Task ApplyAsync_WhenCalledRepeatedlyWithSameSettings_IsIdempotent()
    {
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);
        var settings = new AppSettings { TelemetryCustomEndpoint = "https://collector.example.com:4318" };

        await runtime.ApplyAsync(settings);
        await runtime.ApplyAsync(settings);
        await runtime.ApplyAsync(settings);

        Assert.Single(manager.Applied);
    }

    [Fact]
    public async Task ApplyAsync_KeepsServiceInstanceIdStableAcrossReconfiguration()
    {
        // 只改端点不应让后端把本进程识别成一个新实例，否则实例维度会随用户改配置而碎裂。
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://first.example.com:4318" });
        await runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://second.example.com:4318" });

        Assert.Equal(2, manager.Applied.Count);
        Assert.Equal(
            manager.Applied[0].ResourceAttributes[SemanticConventions.Resource.ServiceInstanceId],
            manager.Applied[1].ResourceAttributes[SemanticConventions.Resource.ServiceInstanceId]);
    }

    [Fact]
    public async Task ApplyAsync_WhenReconfigureThrows_DoesNotPropagate()
    {
        // 遥测是旁路能力：重建失败不得让设置保存或应用启动失败。
        var manager = new RecordingTelemetryManager { ThrowOnReconfigure = true };
        var runtime = CreateRuntime(manager);

        var exception = await Record.ExceptionAsync(
            () => runtime.ApplyAsync(new AppSettings()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ApplyAsync_WhenReconfigureThrows_RetriesOnNextApply()
    {
        // 失败后不得把失败的配置记成"已生效"，否则同一份配置再也不会被重试。
        var manager = new RecordingTelemetryManager { ThrowOnReconfigure = true };
        var runtime = CreateRuntime(manager);
        var settings = new AppSettings { TelemetryCustomEndpoint = "https://collector.example.com:4318" };

        await runtime.ApplyAsync(settings);
        manager.ThrowOnReconfigure = false;
        await runtime.ApplyAsync(settings);

        Assert.Single(manager.Applied);
        Assert.Equal("https://collector.example.com:4318", manager.Applied[0].OtlpEndpoint);
    }

    [Fact]
    public async Task ApplyAsync_WhenAppliesOverlap_ConvergesOnTheLatestSnapshot()
    {
        // 连续变更（用户连按几次保存）必须收敛到最后一次，且不得并发重建 provider。
        var manager = new RecordingTelemetryManager { BlockInsideReconfigure = true };
        var runtime = CreateRuntime(manager);

        var first = runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://first.example.com:4318" });
        Assert.True(
            await manager.ReconfigureEntered.WaitAsync(TimeSpan.FromSeconds(5)),
            "The first apply never reached the manager.");

        var second = runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://second.example.com:4318" });
        var third = runtime.ApplyAsync(new AppSettings { TelemetryCustomEndpoint = "https://third.example.com:4318" });

        manager.ReleaseReconfigure();
        await Task.WhenAll(first, second, third);

        Assert.Equal("https://third.example.com:4318", manager.Applied[^1].OtlpEndpoint);
        Assert.Equal(1, manager.MaxConcurrentReconfigures);
    }

    [Fact]
    public async Task ShutdownAsync_ShutsDownManager()
    {
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ShutdownAsync();

        Assert.Equal(1, manager.ShutdownCount);
    }

    [Fact]
    public async Task ShutdownAsync_DoesNotWaitForExport()
    {
        // issue #126：默认超时是「每个 provider」5000ms 且串行，端点不可达时实测把关闭
        // 拖到 10s 以上。关闭路径必须传非阻塞超时，否则用户为不可达端点买单。
        // 断言复用契约常量而非魔法数 0。
        var manager = new RecordingTelemetryManager();
        var runtime = CreateRuntime(manager);

        await runtime.ShutdownAsync();

        Assert.Equal(
            new[] { ITelemetryManager.NonBlockingShutdownTimeoutMilliseconds },
            manager.ShutdownTimeouts);
    }

    [Fact]
    public async Task ShutdownAsync_WhenManagerThrows_DoesNotPropagate()
    {
        // 关闭路径不得抛：flush 失败不应阻塞进程退出。
        var manager = new RecordingTelemetryManager { ThrowOnShutdown = true };
        var runtime = CreateRuntime(manager);

        var exception = await Record.ExceptionAsync(() => runtime.ShutdownAsync());

        Assert.Null(exception);
    }

    private static TelemetryRuntime CreateRuntime(RecordingTelemetryManager manager)
        => new(
            manager,
            SamplingSettings.CreateDesktopDefaults,
            NullLogger<TelemetryRuntime>.Instance,
            serviceVersion: "1.2.3");

    private sealed class RecordingTelemetryManager : ITelemetryManager
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _reconfigureEntered = new(0);
        private readonly SemaphoreSlim _releaseReconfigure = new(0);
        private int _concurrentReconfigures;

        public List<TelemetrySettings> Applied { get; } = new();

        public int ShutdownCount { get; private set; }

        public List<int> ShutdownTimeouts { get; } = new();

        public int MaxConcurrentReconfigures { get; private set; }

        public bool ThrowOnReconfigure { get; set; }

        public bool ThrowOnShutdown { get; set; }

        public bool BlockInsideReconfigure { get; set; }

        public SemaphoreSlim ReconfigureEntered => _reconfigureEntered;

        public void ReleaseReconfigure() => _releaseReconfigure.Release(int.MaxValue / 2);

        public TracerProvider? TracerProvider => null;

        public MeterProvider? MeterProvider => null;

        public bool IsEnabled => false;

        public void Initialize()
        {
        }

        public void Reconfigure(TelemetrySettings newSettings)
        {
            var concurrent = Interlocked.Increment(ref _concurrentReconfigures);
            lock (_sync)
            {
                MaxConcurrentReconfigures = Math.Max(MaxConcurrentReconfigures, concurrent);
            }

            try
            {
                if (BlockInsideReconfigure)
                {
                    _reconfigureEntered.Release();
                    _releaseReconfigure.Wait(TimeSpan.FromSeconds(10));
                }

                if (ThrowOnReconfigure)
                {
                    throw new InvalidOperationException("exporter build failed");
                }

                lock (_sync)
                {
                    Applied.Add(newSettings);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentReconfigures);
            }
        }

        public bool Shutdown(int timeoutMilliseconds = 5000)
        {
            if (ThrowOnShutdown)
            {
                throw new InvalidOperationException("shutdown failed");
            }

            lock (_sync)
            {
                ShutdownCount++;
                ShutdownTimeouts.Add(timeoutMilliseconds);
            }

            return true;
        }

        public bool Flush(int timeoutMilliseconds = 5000) => true;
    }
}

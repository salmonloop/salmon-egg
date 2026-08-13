using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

/// <summary>
/// 配置合并优先级与"是否需要重建管线"的判定。
/// </summary>
/// <remarks>
/// 本类改写进程级 OTEL_* 环境变量；程序集已设 <c>DisableTestParallelization</c>，
/// 且每个测试结束时恢复原值，因此不会互相污染。
/// </remarks>
public sealed class TelemetrySettingsTests : IDisposable
{
    private static readonly string[] OwnedVariables =
    {
        "OTEL_SDK_DISABLED",
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_SERVICE_NAME",
        "OTEL_ENVIRONMENT"
    };

    private readonly Dictionary<string, string?> _originalValues = new();

    public TelemetrySettingsTests()
    {
        foreach (var name in OwnedVariables)
        {
            _originalValues[name] = Environment.GetEnvironmentVariable(name);
            // 从干净状态起步：开发机上预设的 OTEL_* 会让优先级断言变得不可复现。
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _originalValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Fact]
    public void Build_WhenUserSetsEndpoint_OverridesEnvironmentAndDefault()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://from-env.example.com:4318");

        var settings = Build(new AppSettings { TelemetryCustomEndpoint = "https://from-user.example.com:4318" });

        Assert.Equal("https://from-user.example.com:4318", settings.OtlpEndpoint);
    }

    [Fact]
    public void Build_WhenOnlyEnvironmentSetsEndpoint_OverridesDefault()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://from-env.example.com:4318");

        var settings = Build(new AppSettings());

        Assert.Equal("https://from-env.example.com:4318", settings.OtlpEndpoint);
    }

    [Fact]
    public void Build_WhenNothingOverridesEndpoint_UsesDefault()
    {
        var settings = Build(new AppSettings());

        Assert.Equal("https://otlp.shangxin.me", settings.OtlpEndpoint);
    }

    [Fact]
    public void Build_WhenUserOptsOut_DisablesSdk()
    {
        var settings = Build(new AppSettings { TelemetrySharingEnabled = false });

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void Build_WhenEnvironmentDisablesSdk_DisablesSdk()
    {
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        var settings = Build(new AppSettings { TelemetrySharingEnabled = true });

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void Build_WhenUserOptedIn_EnablesSdkByDefault()
    {
        var settings = Build(new AppSettings());

        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Build_UsesTheSameInstanceIdForEveryBuildInTheProcess()
    {
        // 只改端点不得让后端把本进程识别成新实例，否则实例维度会随用户改配置而碎裂。
        var first = Build(new AppSettings { TelemetryCustomEndpoint = "https://first.example.com:4318" });
        var second = Build(new AppSettings { TelemetryCustomEndpoint = "https://second.example.com:4318" });

        Assert.Equal(
            first.ResourceAttributes[SemanticConventions.Resource.ServiceInstanceId],
            second.ResourceAttributes[SemanticConventions.Resource.ServiceInstanceId]);
    }

    [Fact]
    public void CreateInactiveBootstrap_IsDisabled()
    {
        // 容器构建阶段必须"未激活"：真实用户设置（可能是已关闭）加载前不得导出任何数据。
        var bootstrap = TelemetrySettings.CreateInactiveBootstrap();

        Assert.False(bootstrap.Enabled);
    }

    [Fact]
    public void CreateInactiveBootstrap_DoesNotAdoptEnvironmentEndpoint()
    {
        // 用 Build(null, …) 代替 bootstrap 的话，环境变量会让它判定为启用，
        // 于是在用户设置加载之前就开始导出——用户的关闭意图会被短暂违反。
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://from-env.example.com:4318");

        var bootstrap = TelemetrySettings.CreateInactiveBootstrap();

        Assert.False(bootstrap.Enabled);
        Assert.Null(bootstrap.OtlpEndpoint);
    }

    [Fact]
    public void IsEquivalentTo_WhenOnlyUnrelatedSettingsDiffer_IsTrue()
    {
        // 改主题也会走同一条投影：判为等价才能避免白重建一次管线（含 flush 等待）。
        var left = Build(new AppSettings { Theme = "Light", TelemetryCustomEndpoint = "https://a.example.com:4318" });
        var right = Build(new AppSettings { Theme = "Dark", TelemetryCustomEndpoint = "https://a.example.com:4318" });

        Assert.True(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenEndpointDiffers_IsFalse()
    {
        var left = Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" });
        var right = Build(new AppSettings { TelemetryCustomEndpoint = "https://b.example.com:4318" });

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenOnlyHeadersDiffer_IsFalse()
    {
        // 凭证轮换必须触发重建，否则导出继续用旧凭证被 401 拒绝。
        var left = Build(new AppSettings
        {
            TelemetryCustomEndpoint = "https://a.example.com:4318",
            TelemetryAuthHeader = "api-key=old"
        });
        var right = Build(new AppSettings
        {
            TelemetryCustomEndpoint = "https://a.example.com:4318",
            TelemetryAuthHeader = "api-key=new"
        });

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenBothDisabled_IgnoresOtherFields()
    {
        // 已禁用即无管线可言；否则"改端点后再关开关"会被判为不同而白重建一次。
        var left = Build(new AppSettings
        {
            TelemetrySharingEnabled = false,
            TelemetryCustomEndpoint = "https://a.example.com:4318"
        });
        var right = Build(new AppSettings
        {
            TelemetrySharingEnabled = false,
            TelemetryCustomEndpoint = "https://b.example.com:4318"
        });

        Assert.True(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenEnabledStateDiffers_IsFalse()
    {
        var enabled = Build(new AppSettings { TelemetrySharingEnabled = true });
        var disabled = Build(new AppSettings { TelemetrySharingEnabled = false });

        Assert.False(enabled.IsEquivalentTo(disabled));
    }

    [Fact]
    public void IsEquivalentTo_WhenSamplingDiffers_IsFalse()
    {
        // 采样器在 build 时固化，改了必须重建才会生效。
        var left = TelemetrySettings.Build(new AppSettings(), SamplingSettings.CreateDesktopDefaults());
        var right = TelemetrySettings.Build(new AppSettings(), SamplingSettings.CreateMobileDefaults());

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenServiceVersionDiffers_IsFalse()
    {
        var left = TelemetrySettings.Build(new AppSettings(), SamplingSettings.CreateDesktopDefaults(), "1.0.0");
        var right = TelemetrySettings.Build(new AppSettings(), SamplingSettings.CreateDesktopDefaults(), "2.0.0");

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenOtherIsNull_IsFalse()
    {
        // 首次 apply 时 _appliedSettings 为 null，必须判为"不同"才会真的装配管线。
        var settings = Build(new AppSettings());

        Assert.False(settings.IsEquivalentTo(null));
    }

    private static TelemetrySettings Build(AppSettings userSettings)
        => TelemetrySettings.Build(userSettings, SamplingSettings.CreateDesktopDefaults(), "1.2.3");
}

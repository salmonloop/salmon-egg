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
    // 必须覆盖 Build 读到的**每一个**变量，含各信号专用项：漏掉哪一个，开发机上预设的
    // 那一项就会静默参与解析，让本类的优先级断言不可复现。
    private static readonly string[] OwnedVariables =
    {
        "OTEL_SDK_DISABLED",
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_EXPORTER_OTLP_PROTOCOL",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
        "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_HEADERS",
        "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
        "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL",
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
    public void Build_WhenOnlyEnvironmentSetsEndpoint_AppliesItToEverySignal()
    {
        // 泛用环境变量必须同时供给三个信号：若只落到 traces，metrics/logs 会因未配置而
        // 让 Enabled 判定为 false——部署方设了合法端点却整条管线不启用。
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://from-env.example.com:4318");

        var settings = Build(new AppSettings());

        Assert.Equal("https://from-env.example.com:4318", settings.OtlpEndpoint);
        Assert.Equal("https://from-env.example.com:4318", settings.Traces.Endpoint);
        Assert.Equal("https://from-env.example.com:4318", settings.Metrics.Endpoint);
        Assert.Equal("https://from-env.example.com:4318", settings.Logs.Endpoint);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Build_SignalSpecificEnvironmentOverridesGenericEndpointAndHeaders()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://generic.example.com:4318");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", "x-generic=value");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "https://traces.example.com:4318/v1/traces");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_HEADERS", "x-traces=value");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL", "http/protobuf");

        var settings = Build(new AppSettings());

        Assert.Equal("https://traces.example.com:4318/v1/traces", settings.Traces.Endpoint);
        Assert.Equal("x-traces=value", settings.Traces.Headers);
        Assert.True(settings.Traces.IsSignalSpecificEndpoint);
        Assert.Equal("https://generic.example.com:4318", settings.Metrics.Endpoint);
        Assert.Equal("x-generic=value", settings.Metrics.Headers);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Build_SignalSpecificProtocolOverridesGenericProtocol()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://collector.example.com:4318");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_PROTOCOL", "http/protobuf");

        var settings = Build(new AppSettings());

        Assert.Equal(OtlpProtocol.Grpc, settings.Traces.Protocol);
        Assert.Equal(OtlpProtocol.HttpProtobuf, settings.Logs.Protocol);
    }

    [Fact]
    public void Build_WhenNothingOverridesEndpoint_UsesNoDefaultCollector()
    {
        var settings = Build(new AppSettings());

        Assert.Null(settings.OtlpEndpoint);
        Assert.False(settings.Enabled);
    }

    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")]
    public void Build_WhenAnySingleSignalLacksAnEndpoint_StaysDisabled(string missingVariable)
    {
        // 必须留一个信号缺端点、另两个配好：只配单一信号的写法无法区分"三个都参与判定"与
        // "只判定了 traces"——后者在那种夹具下同样为 false，会把漏判的实现放绿。
        var allVariables = new[]
        {
            "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
            "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
            "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"
        };
        foreach (var variable in allVariables)
        {
            if (!string.Equals(variable, missingVariable, StringComparison.Ordinal))
            {
                Environment.SetEnvironmentVariable(variable, "https://configured.example.com:4318");
            }
        }

        var settings = Build(new AppSettings());

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void Build_WhenUserSetsEndpoint_OverridesSignalSpecificEnvironment()
    {
        // 用户在设置界面填的端点是产品级意图，必须压过部署环境的分信号变量；否则用户明明改了
        // 端点，traces 仍然发往环境变量指定的旧地址。
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "https://from-env.example.com:4318/v1/traces");

        var settings = Build(new AppSettings { TelemetryCustomEndpoint = "https://from-user.example.com:4318" });

        Assert.Equal("https://from-user.example.com:4318", settings.Traces.Endpoint);
        Assert.False(settings.Traces.IsSignalSpecificEndpoint);
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
    public void Build_WhenUserOptedIn_RequiresACollectorEndpoint()
    {
        var settings = Build(new AppSettings());

        Assert.False(settings.Enabled);
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
        var enabled = Build(new AppSettings { TelemetrySharingEnabled = true, TelemetryCustomEndpoint = "https://a.example.com:4318" });
        var disabled = Build(new AppSettings { TelemetrySharingEnabled = false });

        Assert.False(enabled.IsEquivalentTo(disabled));
    }

    [Fact]
    public void IsEquivalentTo_WhenSamplingDiffers_IsFalse()
    {
        // 采样器在 build 时固化，改了必须重建才会生效。
        var left = TelemetrySettings.Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" }, SamplingSettings.CreateDesktopDefaults());
        var right = TelemetrySettings.Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" }, SamplingSettings.CreateMobileDefaults());

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenServiceVersionDiffers_IsFalse()
    {
        var left = TelemetrySettings.Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" }, SamplingSettings.CreateDesktopDefaults(), "1.0.0");
        var right = TelemetrySettings.Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" }, SamplingSettings.CreateDesktopDefaults(), "2.0.0");

        Assert.False(left.IsEquivalentTo(right));
    }

    [Fact]
    public void IsEquivalentTo_WhenOtherIsNull_IsFalse()
    {
        // 首次 apply 时 _appliedSettings 为 null，必须判为"不同"才会真的装配管线。
        var settings = Build(new AppSettings { TelemetryCustomEndpoint = "https://a.example.com:4318" });

        Assert.False(settings.IsEquivalentTo(null));
    }

    private static TelemetrySettings Build(AppSettings userSettings)
        => TelemetrySettings.Build(userSettings, SamplingSettings.CreateDesktopDefaults(), "1.2.3");
}

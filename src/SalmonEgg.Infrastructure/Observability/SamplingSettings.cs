using System;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 采样配置
/// </summary>
public sealed class SamplingSettings
{
    /// <summary>
    /// 正常操作的采样率（0.0 - 1.0）
    /// Desktop/WinUI: 0.1 (10%)
    /// WASM: 0.05 (5%)
    /// Mobile: 0.02 (2%)
    /// </summary>
    public double NormalRate { get; init; } = 0.1;

    /// <summary>
    /// 错误操作的采样率（0.0 - 1.0）
    /// 所有平台: 1.0 (100%) - 错误必须全采集
    /// </summary>
    public double ErrorRate { get; init; } = 1.0;

    /// <summary>
    /// 慢操作阈值（毫秒）
    /// 超过此阈值的操作使用 SlowOperationRate
    /// </summary>
    public long SlowOperationThresholdMs { get; init; } = 3000;

    /// <summary>
    /// 慢操作的采样率（0.0 - 1.0）
    /// Desktop/WinUI: 0.5 (50%)
    /// WASM/Mobile: 0.3 (30%)
    /// </summary>
    public double SlowOperationRate { get; init; } = 0.5;

    /// <summary>
    /// 非常慢操作阈值（毫秒）
    /// 超过此阈值的操作使用 VerySlowOperationRate
    /// </summary>
    public long VerySlowOperationThresholdMs { get; init; } = 10000;

    /// <summary>
    /// 非常慢操作的采样率（0.0 - 1.0）
    /// Desktop/WinUI: 1.0 (100%)
    /// WASM/Mobile: 0.8 (80%)
    /// </summary>
    public double VerySlowOperationRate { get; init; } = 1.0;

    /// <summary>
    /// 关键操作名称列表（如 SessionStart, ChatSubmit）
    /// 这些操作使用更高的采样率
    /// </summary>
    public string[] CriticalOperations { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 关键操作的采样率（0.0 - 1.0）
    /// </summary>
    public double CriticalOperationRate { get; init; } = 0.5;

    /// <summary>
    /// 创建 Desktop/WinUI 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateDesktopDefaults() => new()
    {
        NormalRate = 0.1,
        ErrorRate = 1.0,
        SlowOperationThresholdMs = 3000,
        SlowOperationRate = 0.5,
        VerySlowOperationThresholdMs = 10000,
        VerySlowOperationRate = 1.0,
        CriticalOperationRate = 0.5,
        CriticalOperations = new[] { "SessionStart", "ChatSubmit", "ChatComplete" }
    };

    /// <summary>
    /// 创建 WASM 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateWasmDefaults() => new()
    {
        NormalRate = 0.05,  // WASM 网络受限
        ErrorRate = 1.0,
        SlowOperationThresholdMs = 3000,
        SlowOperationRate = 0.3,  // WASM 降低慢操作采样率
        VerySlowOperationThresholdMs = 10000,
        VerySlowOperationRate = 0.8,
        CriticalOperationRate = 0.3,
        CriticalOperations = new[] { "SessionStart", "ChatSubmit", "ChatComplete" }
    };

    /// <summary>
    /// 创建 Mobile 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateMobileDefaults() => new()
    {
        NormalRate = 0.02,  // Mobile 最低采样率（省电）
        ErrorRate = 1.0,
        SlowOperationThresholdMs = 3000,
        SlowOperationRate = 0.3,
        VerySlowOperationThresholdMs = 10000,
        VerySlowOperationRate = 0.8,
        CriticalOperationRate = 0.3,
        CriticalOperations = new[] { "SessionStart", "ChatSubmit", "ChatComplete" }
    };
}

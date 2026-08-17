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
    /// 判断两份采样配置是否等价（供 <see cref="TelemetrySettings.IsEquivalentTo"/> 判断
    /// 是否需要重建 provider；采样器在 build 时固化，改了必须重建）。
    /// </summary>
    public bool IsEquivalentTo(SamplingSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return NormalRate.Equals(other.NormalRate);
    }

    /// <summary>
    /// 创建 Desktop/WinUI 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateDesktopDefaults() => new()
    {
        NormalRate = 0.1
    };

    /// <summary>
    /// 创建 WASM 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateWasmDefaults() => new()
    {
        NormalRate = 0.05  // WASM 网络受限
    };

    /// <summary>
    /// 创建 Mobile 的默认采样配置
    /// </summary>
    public static SamplingSettings CreateMobileDefaults() => new()
    {
        NormalRate = 0.02  // Mobile 最低采样率（省电）
    };
}

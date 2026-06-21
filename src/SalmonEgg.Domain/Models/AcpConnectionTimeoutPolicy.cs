using System;

namespace SalmonEgg.Domain.Models;

public static class AcpConnectionTimeoutPolicy
{
    public const int DefaultSeconds = 120;
    public const int MinimumSeconds = 1;
    public const int MaximumSeconds = 600;

    public static int ResolveSeconds(int configuredSeconds)
        => configuredSeconds > 0 ? configuredSeconds : DefaultSeconds;

    public static TimeSpan ResolveTimeout(int configuredSeconds)
        => TimeSpan.FromSeconds(ResolveSeconds(configuredSeconds));
}

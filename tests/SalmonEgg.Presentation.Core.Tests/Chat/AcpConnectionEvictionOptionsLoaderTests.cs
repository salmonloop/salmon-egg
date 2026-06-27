using System;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpConnectionEvictionOptionsLoaderTests
{
    private const string EnableEnv = "SALMONEGG_ACP_EVICTION_ENABLED";
    private const string IdleTtlMinutesEnv = "SALMONEGG_ACP_EVICTION_IDLE_TTL_MINUTES";
    private const string MaxWarmProfilesEnv = "SALMONEGG_ACP_EVICTION_MAX_WARM_PROFILES";
    private const string MaxPinnedProfilesEnv = "SALMONEGG_ACP_EVICTION_MAX_PINNED_PROFILES";

    [Fact]
    public void LoadEnvironmentDefaults_UsesNoIoDefaultsWhenEnvMissing()
    {
        ClearEnvironment();

        var options = AcpConnectionEvictionOptionsLoader.LoadEnvironmentDefaults(Mock.Of<ILogger>());

        Assert.False(options.EnablePolicyEviction);
        Assert.Null(options.IdleTtl);
        Assert.Null(options.MaxWarmProfiles);
        Assert.Null(options.MaxPinnedProfiles);
    }

    [Fact]
    public void LoadEnvironmentDefaults_EnvOverridesNoIoDefaults()
    {
        Environment.SetEnvironmentVariable(EnableEnv, "true");
        Environment.SetEnvironmentVariable(IdleTtlMinutesEnv, "9");
        Environment.SetEnvironmentVariable(MaxWarmProfilesEnv, "2");
        Environment.SetEnvironmentVariable(MaxPinnedProfilesEnv, "1");
        try
        {
            var options = AcpConnectionEvictionOptionsLoader.LoadEnvironmentDefaults(Mock.Of<ILogger>());

            Assert.True(options.EnablePolicyEviction);
            Assert.Equal(TimeSpan.FromMinutes(9), options.IdleTtl);
            Assert.Equal(2, options.MaxWarmProfiles);
            Assert.Equal(1, options.MaxPinnedProfiles);
        }
        finally
        {
            ClearEnvironment();
        }
    }

    private static void ClearEnvironment()
    {
        Environment.SetEnvironmentVariable(EnableEnv, null);
        Environment.SetEnvironmentVariable(IdleTtlMinutesEnv, null);
        Environment.SetEnvironmentVariable(MaxWarmProfilesEnv, null);
        Environment.SetEnvironmentVariable(MaxPinnedProfilesEnv, null);
    }
}

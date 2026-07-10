using System;
using Xunit;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class AcpConnectionTimeoutPolicyTests
{
    [Fact]
    public void ResolveTimeout_WhenConfigurationMissing_UsesSharedDefault()
    {
        var timeout = AcpConnectionTimeoutPolicy.ResolveTimeout(0);

        Assert.Equal(TimeSpan.FromSeconds(AcpConnectionTimeoutPolicy.DefaultSeconds), timeout);
    }
}

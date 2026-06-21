using System;
using NUnit.Framework;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Models;

[TestFixture]
public sealed class AcpConnectionTimeoutPolicyTests
{
    [Test]
    public void ResolveTimeout_WhenConfigurationMissing_UsesSharedDefault()
    {
        var timeout = AcpConnectionTimeoutPolicy.ResolveTimeout(0);

        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(AcpConnectionTimeoutPolicy.DefaultSeconds)));
    }
}

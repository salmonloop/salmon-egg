using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the tester's own responsibility: reject a plan that cannot possibly start before spending a
/// process on it, and otherwise hand the plan to the handshake unchanged.
/// </summary>
public sealed class DesktopAcpSetupConnectivityTesterTests
{
    [Fact]
    public void Constructor_WithNullProbe_ShouldThrow()
        => Assert.Throws<ArgumentNullException>(() => new DesktopAcpSetupConnectivityTester(null!));

    [Fact]
    public async Task TestAsync_WithNullPlan_ShouldThrow()
    {
        var tester = new DesktopAcpSetupConnectivityTester(
            new StubAcpSetupHandshakeProbe(AcpSetupTestResult.Success(1, "agent")));

        await Assert.ThrowsAsync<ArgumentNullException>(() => tester.TestAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TestAsync_WithBlankCommand_ShouldFailAtValidationWithoutProbing(string command)
    {
        var handshake = new StubAcpSetupHandshakeProbe(AcpSetupTestResult.Success(1, "agent"));
        var tester = new DesktopAcpSetupConnectivityTester(handshake);

        var result = await tester.TestAsync(AcpSetupFixtures.Plan(command));

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.Validation, result.Stage);
        Assert.Equal(0, handshake.ProbeCount);
    }

    [Fact]
    public async Task TestAsync_WithRunnablePlan_ShouldForwardPlanToHandshake()
    {
        var handshake = new StubAcpSetupHandshakeProbe(AcpSetupTestResult.Success(1, "Test Agent"));
        var tester = new DesktopAcpSetupConnectivityTester(handshake);
        var plan = AcpSetupFixtures.Plan("npx", "@scope/adapter", "--acp");

        var result = await tester.TestAsync(plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.Completed, result.Stage);
        Assert.Equal("Test Agent", result.AgentName);
        Assert.Equal(1, handshake.ProbeCount);
        Assert.Same(plan, handshake.LastPlan);
    }

    [Fact]
    public async Task TestAsync_WhenHandshakeFails_ShouldSurfaceItsStage()
    {
        var handshake = new StubAcpSetupHandshakeProbe(
            AcpSetupTestResult.Failure(AcpSetupTestStage.AdapterStartup, "boom", "hint"));
        var tester = new DesktopAcpSetupConnectivityTester(handshake);

        var result = await tester.TestAsync(AcpSetupFixtures.Plan());

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.AdapterStartup, result.Stage);
        Assert.Equal("boom", result.ErrorDetail);
        Assert.Equal("hint", result.RemediationKey);
    }
}

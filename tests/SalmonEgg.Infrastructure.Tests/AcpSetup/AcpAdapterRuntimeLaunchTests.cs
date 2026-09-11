using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Infrastructure.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

public sealed class AcpAdapterRuntimeLaunchTests
{
    [Theory]
    [InlineData("claude-code", "claude", "CLAUDE_CODE_EXECUTABLE")]
    [InlineData("codex", "codex", "CODEX_PATH")]
    public void BuildLaunchPlan_WithRuntimeOverride_PassesPathToAdapter(
        string agentId,
        string runtimeCommand,
        string environmentVariable)
    {
        // Arrange
        var runtimePath = "/custom agent/bin/" + runtimeCommand;
        var draft = CreateDraft(
            agentId,
            AcpCommandOverrides.Create(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [runtimeCommand] = runtimePath
            }));

        // Act
        var plan = draft.BuildLaunchPlan();

        // Assert
        Assert.Equal(draft.Adapter.Component.ProbeCommand, plan.Command);
        Assert.True(plan.Environment.TryGetValue(environmentVariable, out var executable));
        Assert.Equal(runtimePath, executable);
    }

    [Theory]
    [InlineData("claude-code", "CLAUDE_CODE_EXECUTABLE")]
    [InlineData("codex", "CODEX_PATH")]
    public void BuildLaunchPlan_WithoutRuntimeOverride_PreservesAdapterRuntimeSelection(
        string agentId,
        string environmentVariable)
    {
        // Arrange
        var draft = CreateDraft(agentId, AcpCommandOverrides.Empty);

        // Act
        var plan = draft.BuildLaunchPlan();

        // Assert
        Assert.Equal(draft.Adapter.Component.ProbeCommand, plan.Command);
        Assert.False(plan.Environment.ContainsKey(environmentVariable));
    }

    [Theory]
    [InlineData(@"C:\Users\Agent\claude.cmd")]
    [InlineData(@"C:\Users\Agent\claude.BAT")]
    [InlineData("C:/Agent Tools/claude.CMD")]
    [InlineData(@"\\server\tools\claude.cmd")]
    [InlineData("//server/tools/claude.bat")]
    [InlineData(@".\claude.cmd")]
    public async Task TestDraft_ClaudeWindowsBatchRuntime_ReturnsValidationBeforeLaunching(string runtimePath)
    {
        // Arrange
        var draft = CreateRuntimeDraft("claude-code", runtimePath);
        var tester = new Mock<IAcpSetupConnectivityTester>(MockBehavior.Strict);
        var orchestrator = CreateOrchestrator(tester.Object, Mock.Of<IConfigurationService>());

        // Act
        var result = await orchestrator.TestDraftAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.Validation, result.Stage);
        Assert.Equal(AcpSetupParameterValidator.RuntimeBatchLauncherNotSupportedKey, result.RemediationKey);
        Assert.Null(result.ErrorDetail);
        Assert.Equal(runtimePath, draft.CommandOverrides.Resolve("claude"));
        tester.Verify(value => value.TestAsync(It.IsAny<AcpLaunchPlan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("claude-code", @"C:\Program Files\Claude\claude.exe")]
    [InlineData("claude-code", "/opt/agent/claude")]
    [InlineData("claude-code", "/opt/agent/claude.cmd")]
    [InlineData("claude-code", "///opt/agent/claude.cmd")]
    [InlineData("claude-code", "/opt/agent/cli.js")]
    [InlineData("claude-code", @"C:\agent\cli.js")]
    [InlineData("codex", @"C:\Users\Agent\codex.cmd")]
    [InlineData("codex", @"C:\Users\Agent\codex.bat")]
    public async Task TestDraft_NoKnownBatchRestriction_DelegatesUnchangedPlan(string agentId, string runtimePath)
    {
        // Arrange
        var draft = CreateRuntimeDraft(agentId, runtimePath);
        AcpLaunchPlan? testedPlan = null;
        var tester = new Mock<IAcpSetupConnectivityTester>();
        tester.Setup(value => value.TestAsync(It.IsAny<AcpLaunchPlan>(), It.IsAny<CancellationToken>()))
            .Callback<AcpLaunchPlan, CancellationToken>((plan, _) => testedPlan = plan)
            .ReturnsAsync(AcpSetupTestResult.Success(1, agentId));
        var orchestrator = CreateOrchestrator(tester.Object, Mock.Of<IConfigurationService>());

        // Act
        var result = await orchestrator.TestDraftAsync(draft, TestContext.Current.CancellationToken);

        // Assert: allowing the existing tester is not a claim that a particular executable works.
        Assert.True(result.IsSuccess);
        Assert.NotNull(testedPlan);
        Assert.Equal(runtimePath, testedPlan.Environment[draft.Adapter.RuntimeCommandEnvironmentVariable]);
        tester.Verify(value => value.TestAsync(It.IsAny<AcpLaunchPlan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveDraft_ClaudeWindowsBatchRuntime_CannotSaveAnInvalidVerdict(bool claimedVerified)
    {
        // Arrange
        var verification = claimedVerified ? ProfileVerification.Verified(DateTimeOffset.UtcNow) : ProfileVerification.Unverified;
        var draft = CreateRuntimeDraft("claude-code", @"C:\Agent\claude.cmd", verification);
        var configuration = new Mock<IConfigurationService>(MockBehavior.Strict);
        var tester = new Mock<IAcpSetupConnectivityTester>(MockBehavior.Strict);
        var orchestrator = CreateOrchestrator(tester.Object, configuration.Object);

        // Act
        var result = await orchestrator.SaveDraftAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupParameterValidator.RuntimeBatchLauncherNotSupportedKey, result.Error);
        configuration.Verify(value => value.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
        tester.Verify(value => value.TestAsync(It.IsAny<AcpLaunchPlan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(@"C:\Agent\claude.exe")]
    [InlineData(null)]
    public async Task SaveDraft_ValidUnverifiedRuntime_PreservesExplicitChoice(string? runtimePath)
    {
        // Arrange
        var draft = CreateRuntimeDraft("claude-code", runtimePath, ProfileVerification.Unverified);
        ServerConfiguration? saved = null;
        var configuration = new Mock<IConfigurationService>();
        configuration.Setup(value => value.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()))
            .Callback<ServerConfiguration>(value => saved = value).Returns(Task.CompletedTask);
        var tester = new Mock<IAcpSetupConnectivityTester>(MockBehavior.Strict);
        var orchestrator = CreateOrchestrator(tester.Object, configuration.Object);

        // Act
        var result = await orchestrator.SaveDraftAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(saved, result.Value);
        Assert.Equal(ProfileVerification.Unverified, result.Value.Verification);
        Assert.Equal(runtimePath is not null, result.Value.StdioEnvironment.ContainsKey("CLAUDE_CODE_EXECUTABLE"));
        if (runtimePath is not null) Assert.Equal(runtimePath, result.Value.StdioEnvironment["CLAUDE_CODE_EXECUTABLE"]);
        tester.Verify(value => value.TestAsync(It.IsAny<AcpLaunchPlan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AcpSetupWizardOrchestrator CreateOrchestrator(
        IAcpSetupConnectivityTester tester, IConfigurationService configuration)
        => new(new AcpAgentCatalog(), new StubAcpExecutableProbe(), Mock.Of<IAcpComponentInstaller>(), tester, configuration);

    private static AcpSetupDraft CreateRuntimeDraft(
        string agentId, string? runtimePath, ProfileVerification verification = default)
    {
        var agent = new AcpAgentCatalog().FindAgent(agentId)!;
        return new AcpSetupDraft
        {
            Agent = agent,
            Adapter = agent.ResolveRecommendedAdapter()!,
            ParameterValues = new Dictionary<string, string>(StringComparer.Ordinal),
            ProfileName = agent.DisplayName,
            Verification = verification,
            CommandOverrides = runtimePath is null ? AcpCommandOverrides.Empty
                : AcpCommandOverrides.Create(new Dictionary<string, string> { [agent.Runtime.ProbeCommand] = runtimePath })
        };
    }

    private static AcpSetupDraft CreateDraft(string agentId, AcpCommandOverrides overrides)
    {
        var agent = new AcpAgentCatalog().FindAgent(agentId)!;
        return new AcpSetupDraft
        {
            Agent = agent,
            Adapter = agent.ResolveRecommendedAdapter()!,
            ParameterValues = new Dictionary<string, string>(StringComparer.Ordinal),
            ProfileName = agent.DisplayName,
            CommandOverrides = overrides
        };
    }
}

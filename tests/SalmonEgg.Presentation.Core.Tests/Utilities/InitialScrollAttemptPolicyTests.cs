using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class InitialScrollAttemptPolicyTests
{
    private const int ViewMaxAttempts = 8;

    [Fact]
    public void Decide_ReturnsStop_WhenNoMessagesRemain()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: false,
            autoScrollEnabled: true,
            reachedBottom: false,
            attempt: 0,
            maxAttempts: 3);

        Assert.Equal(InitialScrollAttemptOutcome.Stop, result);
    }

    [Fact]
    public void Decide_ReturnsStop_WhenUserDisabledAutoScroll()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: false,
            reachedBottom: false,
            attempt: 0,
            maxAttempts: 3);

        Assert.Equal(InitialScrollAttemptOutcome.Stop, result);
    }

    [Fact]
    public void Decide_ReturnsComplete_WhenBottomReached()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: true,
            reachedBottom: true,
            attempt: 1,
            maxAttempts: 3);

        Assert.Equal(InitialScrollAttemptOutcome.Complete, result);
    }

    [Fact]
    public void Decide_ReturnsRetry_WhenBottomNotReached_AndAttemptsRemain()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: true,
            reachedBottom: false,
            attempt: 1,
            maxAttempts: 3);

        Assert.Equal(InitialScrollAttemptOutcome.Retry, result);
    }

    [Fact]
    public void Decide_ReturnsStop_WhenBottomNotReached_AndAttemptsExhausted()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: true,
            reachedBottom: false,
            attempt: 3,
            maxAttempts: 3);

        Assert.Equal(InitialScrollAttemptOutcome.Stop, result);
    }

    [Fact]
    public void Decide_ReturnsRetry_OnLastViewRetryBeforeAttemptsAreExhausted()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: true,
            reachedBottom: false,
            attempt: ViewMaxAttempts - 1,
            maxAttempts: ViewMaxAttempts);

        Assert.Equal(InitialScrollAttemptOutcome.Retry, result);
    }

    [Fact]
    public void Decide_ReturnsStop_WhenViewRetryBudgetIsExhausted()
    {
        var result = InitialScrollAttemptPolicy.Decide(
            hasMessages: true,
            autoScrollEnabled: true,
            reachedBottom: false,
            attempt: ViewMaxAttempts,
            maxAttempts: ViewMaxAttempts);

        Assert.Equal(InitialScrollAttemptOutcome.Stop, result);
    }
}

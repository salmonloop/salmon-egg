using System;
using System.Threading;
using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class AsyncQueryCoordinatorTests
{
    [Fact]
    public void Begin_ReturnsFirstTicketWithIncrementedVersion()
    {
        using var coordinator = new AsyncQueryCoordinator();

        var ticket = coordinator.Begin();

        Assert.Equal(1, ticket.Version);
        Assert.True(coordinator.IsActive(ticket));
        Assert.False(ticket.Token.IsCancellationRequested);
    }

    [Fact]
    public void Begin_CancelsPreviousTicketBeforeIssuingNext()
    {
        using var coordinator = new AsyncQueryCoordinator();

        var first = coordinator.Begin();
        var second = coordinator.Begin();

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(coordinator.IsActive(first));
        Assert.True(coordinator.IsActive(second));
    }

    [Fact]
    public void Cancel_MarksActiveTicketInactive()
    {
        using var coordinator = new AsyncQueryCoordinator();
        var ticket = coordinator.Begin();
        Assert.True(coordinator.IsActive(ticket));

        coordinator.Cancel();

        Assert.True(ticket.Token.IsCancellationRequested);
        Assert.False(coordinator.IsActive(ticket));
    }

    [Fact]
    public void Cancel_WhenNoActiveQuery_IsNoOp()
    {
        using var coordinator = new AsyncQueryCoordinator();

        coordinator.Cancel();

        var ticket = coordinator.Begin();
        Assert.True(coordinator.IsActive(ticket));
    }

    [Fact]
    public void Dispose_MakesIsActiveFalseAndPreventsFurtherBegins()
    {
        var coordinator = new AsyncQueryCoordinator();
        var ticket = coordinator.Begin();

        coordinator.Dispose();

        Assert.False(coordinator.IsActive(ticket));
        Assert.Throws<ObjectDisposedException>(() => coordinator.Begin());
    }

    [Fact]
    public void Dispose_IsIdempotentAndCancelAfterDisposeIsSafe()
    {
        var coordinator = new AsyncQueryCoordinator();

        coordinator.Dispose();
        coordinator.Dispose();
        coordinator.Cancel();

        Assert.Throws<ObjectDisposedException>(() => coordinator.Begin());
    }
}

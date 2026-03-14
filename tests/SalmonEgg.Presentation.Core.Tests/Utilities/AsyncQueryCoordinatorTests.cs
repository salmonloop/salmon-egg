using System;
using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class AsyncQueryCoordinatorTests
{
    [Fact]
    public void Begin_ShouldReturnActiveTicket()
    {
        using var coordinator = new AsyncQueryCoordinator();
        var ticket = coordinator.Begin();

        Assert.True(coordinator.IsActive(ticket));
    }

    [Fact]
    public void Begin_ShouldInvalidatePreviousTicket()
    {
        using var coordinator = new AsyncQueryCoordinator();
        var first = coordinator.Begin();
        var second = coordinator.Begin();

        Assert.False(coordinator.IsActive(first));
        Assert.True(coordinator.IsActive(second));
    }

    [Fact]
    public void Cancel_ShouldInvalidateTicket()
    {
        using var coordinator = new AsyncQueryCoordinator();
        var ticket = coordinator.Begin();

        coordinator.Cancel();

        Assert.False(coordinator.IsActive(ticket));
    }

    [Fact]
    public void Dispose_ShouldInvalidateTicket()
    {
        var coordinator = new AsyncQueryCoordinator();
        var ticket = coordinator.Begin();

        coordinator.Dispose();

        Assert.False(coordinator.IsActive(ticket));
    }

    [Fact]
    public void Begin_AfterDispose_Throws()
    {
        var coordinator = new AsyncQueryCoordinator();
        coordinator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => coordinator.Begin());
    }
}

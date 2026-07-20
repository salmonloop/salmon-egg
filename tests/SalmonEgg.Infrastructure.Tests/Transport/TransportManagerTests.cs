using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class TransportManagerTests
{
    [Fact]
    public async Task RegisterTransportAsync_WithExplicitId_RegistersAndRetrieves()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport();

        var id = await manager.RegisterTransportAsync(transport, "t1");

        Assert.Equal("t1", id);
        Assert.Same(transport, manager.GetTransport("t1"));
        Assert.Equal(1, manager.GetActiveTransportCount());
    }

    [Fact]
    public async Task RegisterTransportAsync_WithoutId_GeneratesUniqueTransportPrefixedId()
    {
        var manager = new TransportManager();

        var id = await manager.RegisterTransportAsync(new FakeTransport());

        Assert.StartsWith("transport_", id);
        Assert.NotNull(manager.GetTransport(id));
    }

    [Fact]
    public async Task RegisterTransportAsync_WithExistingId_DisconnectsAndReplacesPrevious()
    {
        var manager = new TransportManager();
        var first = new FakeTransport();
        var second = new FakeTransport();

        await manager.RegisterTransportAsync(first, "t1");
        await manager.RegisterTransportAsync(second, "t1");

        Assert.Same(second, manager.GetTransport("t1"));
        Assert.True(first.DisconnectInvoked);
        Assert.False(second.DisconnectInvoked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTransport_BlankOrNull_ReturnsNull(string? id)
    {
        var manager = new TransportManager();
        await manager.RegisterTransportAsync(new FakeTransport(), "t1");

        Assert.Null(manager.GetTransport(id!));
    }

    [Fact]
    public async Task DisconnectTransportAsync_RemovesAndCallsUnderlyingDisconnect()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport();
        await manager.RegisterTransportAsync(transport, "t1");

        var result = await manager.DisconnectTransportAsync("t1");

        Assert.True(result);
        Assert.True(transport.DisconnectInvoked);
        Assert.Null(manager.GetTransport("t1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task DisconnectTransportAsync_UnknownOrBlank_ReturnsFalse(string? id)
    {
        var manager = new TransportManager();

        var result = await manager.DisconnectTransportAsync(id!);

        Assert.False(result);
    }

    [Fact]
    public async Task DisconnectTransportAsync_WhenUnderlyingThrows_RemovesAndReturnsFalse()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport(throwOnDisconnect: true);
        await manager.RegisterTransportAsync(transport, "t1");

        var result = await manager.DisconnectTransportAsync("t1");

        Assert.False(result);
        Assert.Null(manager.GetTransport("t1"));
    }

    [Fact]
    public async Task RemoveTransport_RemovesWithoutCallingDisconnect()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport();
        await manager.RegisterTransportAsync(transport, "t1");

        var result = manager.RemoveTransport("t1");

        Assert.True(result);
        Assert.False(transport.DisconnectInvoked);
        Assert.Null(manager.GetTransport("t1"));
    }

    [Fact]
    public async Task GetActiveTransportIds_ReturnsAllRegisteredIds()
    {
        var manager = new TransportManager();
        await manager.RegisterTransportAsync(new FakeTransport(), "a");
        await manager.RegisterTransportAsync(new FakeTransport(), "b");

        var ids = manager.GetActiveTransportIds().OrderBy(x => x).ToArray();

        Assert.Equal(new[] { "a", "b" }, ids);
    }

    [Fact]
    public async Task DisconnectAllTransportsAsync_DisconnectsEachAndReturnsCount()
    {
        var manager = new TransportManager();
        await manager.RegisterTransportAsync(new FakeTransport(), "a");
        await manager.RegisterTransportAsync(new FakeTransport(), "b");

        var count = await manager.DisconnectAllTransportsAsync();

        Assert.Equal(2, count);
        Assert.Equal(0, manager.GetActiveTransportCount());
    }

    [Fact]
    public async Task GetFirstActiveTransport_ReturnsFirstConnectedTransport()
    {
        var manager = new TransportManager();
        var disconnected = new FakeTransport { IsConnected = false };
        var connected = new FakeTransport { IsConnected = true };
        await manager.RegisterTransportAsync(disconnected, "a");
        await manager.RegisterTransportAsync(connected, "b");

        Assert.Same(connected, manager.GetFirstActiveTransport());
        Assert.Equal("b", manager.GetFirstActiveTransportId());
    }

    [Fact]
    public async Task MessageReceived_ForwardsFromRegisteredTransport()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport();
        await manager.RegisterTransportAsync(transport, "t1");

        MessageReceivedEventArgs? captured = null;
        manager.MessageReceived += (_, e) => captured = e;
        transport.RaiseMessageReceived("hello");

        Assert.NotNull(captured);
        Assert.Equal("hello", captured!.Message);
    }

    [Fact]
    public async Task ErrorOccurred_ForwardsFromRegisteredTransport()
    {
        var manager = new TransportManager();
        var transport = new FakeTransport();
        await manager.RegisterTransportAsync(transport, "t1");

        TransportErrorEventArgs? captured = null;
        manager.ErrorOccurred += (_, e) => captured = e;
        var args = new TransportErrorEventArgs { ErrorMessage = "boom" };
        transport.RaiseErrorOccurred(args);

        Assert.NotNull(captured);
        Assert.Equal("boom", captured!.ErrorMessage);
    }

    private sealed class FakeTransport : ITransport
    {
        private readonly bool _throwOnDisconnect;

        public FakeTransport(bool throwOnDisconnect = false)
        {
            _throwOnDisconnect = throwOnDisconnect;
        }

        public bool IsConnected { get; set; }
        public bool DisconnectInvoked { get; private set; }

        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            DisconnectInvoked = true;
            if (_throwOnDisconnect)
            {
                throw new InvalidOperationException("disconnect failed");
            }
            IsConnected = false;
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void RaiseMessageReceived(string message)
            => MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));

        public void RaiseErrorOccurred(TransportErrorEventArgs args)
            => ErrorOccurred?.Invoke(this, args);
    }
}

using System;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class SessionManagerTests
{
    [Fact]
    public async Task CreateSessionAsync_WhenSessionIdMissing_ThrowsEnglishArgumentException()
    {
        var manager = new SessionManager();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateSessionAsync(" "));

        Assert.Equal("sessionId", ex.ParamName);
        Assert.Contains("Session ID cannot be empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSessionAsync_WhenSessionAlreadyExists_ThrowsEnglishInvalidOperationException()
    {
        var manager = new SessionManager();
        await manager.CreateSessionAsync("session-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSessionAsync("session-1"));

        Assert.Contains("Session 'session-1' already exists", ex.Message, StringComparison.Ordinal);
    }
}

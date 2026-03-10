using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Infrastructure;

public sealed class SessionManagerDisplayNameTests
{
    [Fact]
    public async Task CreateSession_SetsDefaultDisplayName()
    {
        var manager = new SessionManager();
        var s = await manager.CreateSessionAsync("1234567890");

        Assert.Equal("会话 12345678", s.DisplayName);
        Assert.Equal("会话 12345678", manager.GetSession("1234567890")!.DisplayName);
    }

    [Fact]
    public async Task UpdateSession_AllowsRenaming()
    {
        var manager = new SessionManager();
        await manager.CreateSessionAsync("abc");

        var ok = manager.UpdateSession("abc", s => s.DisplayName = "My Session");
        Assert.True(ok);
        Assert.Equal("My Session", manager.GetSession("abc")!.DisplayName);
    }
}


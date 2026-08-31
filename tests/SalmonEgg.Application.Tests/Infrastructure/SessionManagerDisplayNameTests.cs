using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Infrastructure;

public sealed class SessionManagerDisplayNameTests
{
    [Fact]
    public async Task CreateSession_SetsDefaultDisplayName()
    {
        var manager = new SessionManager();
        var s = await manager.CreateSessionAsync("1234567890", @"C:\repo\demo");

        Assert.Equal("Session 12345678", s.DisplayName);
        Assert.Equal("Session 12345678", manager.GetSession("1234567890")!.DisplayName);
    }

    [Fact]
    public async Task GetSession_HandsOutLiveReference_SoRenamingIsVisibleThroughTheManager()
    {
        var manager = new SessionManager();
        await manager.CreateSessionAsync("abc", @"C:\repo\demo");

        manager.GetSession("abc")!.DisplayName = "My Session";

        Assert.Equal("My Session", manager.GetSession("abc")!.DisplayName);
    }

    [Fact]
    public async Task Renaming_IsNotSessionActivity()
    {
        var manager = new SessionManager();
        await manager.CreateSessionAsync("abc", @"C:\repo\demo");
        var session = manager.GetSession("abc")!;
        var original = session.LastActivityAt;

        // 活动时间的语义现在内建于每个具名操作:改个显示名不是一次会话活动,
        // 追加历史才是。以前这个区分靠调用方传 updateActivity 标志,容易传错。
        session.DisplayName = "My Session";
        Assert.Equal(original, session.LastActivityAt);

        session.AppendHistory(SessionUpdateEntry.CreateTextMessage("chunk"));
        Assert.True(session.LastActivityAt > original);
    }
}

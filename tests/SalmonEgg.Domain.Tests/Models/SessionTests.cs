using Xunit;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Tests.Models;

/// <summary>
/// 针对 <see cref="Session"/> 自持同步的测试。会话是 pump 线程与 UI 线程真并发触碰的聚合，
/// 因此这里既钉住"并发追加与快照读不会互相撞坏"，也钉住"内部可变状态绝不外泄"——
/// 后者一旦破功，前者提供的同步就会被调用方在锁外的改动绕过。
/// </summary>
public sealed class SessionTests
{
    [Fact]
    public void AppendHistory_FromManyThreads_LosesNoEntry()
    {
        // 这条是并发保护的判定性证据:两个线程同时 List<T>.Add,无锁时会互相覆写槽位、
        // 静默丢条目(甚至撞坏内部 _size),因此计数必然少于应有值。
        // 相比"并发读会不会抛异常",丢条目是确定性的,不依赖时序窗口。
        var session = new Session("s1", @"C:\repo\demo");
        const int writers = 8;
        const int perWriter = 2000;
        using var start = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, writers)
            .Select(w => Task.Run(() =>
            {
                start.Wait();
                for (var i = 0; i < perWriter; i++)
                {
                    session.AppendHistory(SessionUpdateEntry.CreateTextMessage($"w{w}-{i}"));
                }
            }))
            .ToArray();

        start.Set();
        Assert.True(Task.WhenAll(tasks).Wait(TimeSpan.FromSeconds(60)));

        var history = session.SnapshotHistory();
        Assert.Equal(writers * perWriter, history.Count);
        Assert.DoesNotContain(history, entry => entry is null);
        Assert.Equal(writers * perWriter, history.Select(entry => entry.TextContent).Distinct().Count());
    }

    [Fact]
    public void AppendHistory_ConcurrentWithSnapshotHistory_DoesNotThrowOrTear()
    {
        var session = new Session("s1", @"C:\repo\demo");
        const int appendCount = 2000;
        using var start = new ManualResetEventSlim(false);

        var appender = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < appendCount; i++)
            {
                session.AppendHistory(SessionUpdateEntry.CreateTextMessage($"chunk-{i}"));
            }
        });

        var reader = Task.Run(() =>
        {
            start.Wait();
            var observed = 0;
            while (observed < appendCount)
            {
                // 未加同步时,这里会与 List<T>.Add 竞争并抛"集合已被修改"。
                var snapshot = session.SnapshotHistory();
                observed = snapshot.Count;

                // 快照必须是稳定的:枚举它的过程中追加线程仍在写入。
                foreach (var entry in snapshot)
                {
                    Assert.NotNull(entry);
                }
            }
        });

        start.Set();
        Assert.True(Task.WhenAll(appender, reader).Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(appendCount, session.SnapshotHistory().Count);
    }

    [Fact]
    public void SnapshotHistory_ReturnsCopy_SoCallerCannotReachInternalList()
    {
        var session = new Session("s1", @"C:\repo\demo");
        session.AppendHistory(SessionUpdateEntry.CreateTextMessage("kept"));

        var snapshot = session.SnapshotHistory();
        Assert.NotSame(snapshot, session.SnapshotHistory());
        Assert.Single(session.SnapshotHistory());
    }

    [Fact]
    public void SnapshotMode_ReturnsDeepCopy_SoCallerMutationCannotReachAggregate()
    {
        var session = new Session("s1", @"C:\repo\demo");
        session.SetMode(new SessionModeState
        {
            CurrentModeId = "chat",
            AvailableModes = [new SessionMode("chat", "Chat"), new SessionMode("plan", "Plan")]
        });

        var leaked = session.SnapshotMode();
        leaked.CurrentModeId = "plan";
        leaked.AvailableModes.Clear();
        leaked.AvailableModes.Add(new SessionMode("hacked", "Hacked"));

        var actual = session.SnapshotMode();
        Assert.Equal("chat", actual.CurrentModeId);
        Assert.Equal(["chat", "plan"], actual.AvailableModes.Select(mode => mode.Id));
    }

    [Fact]
    public void SetMode_StoresDeepCopy_SoLaterCallerMutationCannotReachAggregate()
    {
        var session = new Session("s1", @"C:\repo\demo");
        var source = new SessionModeState
        {
            CurrentModeId = "chat",
            AvailableModes = [new SessionMode("chat", "Chat")]
        };

        session.SetMode(source);
        source.CurrentModeId = "plan";
        source.AvailableModes.Clear();

        var actual = session.SnapshotMode();
        Assert.Equal("chat", actual.CurrentModeId);
        Assert.Equal(["chat"], actual.AvailableModes.Select(mode => mode.Id));
    }

    [Fact]
    public void SetCurrentModeId_ReResolvesCurrentMode_KeepingModeStateSelfConsistent()
    {
        var session = new Session("s1", @"C:\repo\demo");
        session.SetMode(new SessionModeState
        {
            CurrentModeId = "chat",
            AvailableModes = [new SessionMode("chat", "Chat"), new SessionMode("plan", "Plan")]
        });

        session.SetCurrentModeId("plan");

        var actual = session.SnapshotMode();
        Assert.Equal("plan", actual.CurrentModeId);
        Assert.Equal("Plan", actual.CurrentMode?.Name);
    }

    [Fact]
    public void TryCancel_OnlyOneOfManyConcurrentCallersWins()
    {
        var session = new Session("s1", @"C:\repo\demo");
        using var start = new ManualResetEventSlim(false);

        var winners = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return session.TryCancel();
            }))
            .ToArray();

        start.Set();
        Assert.True(Task.WhenAll(winners).Wait(TimeSpan.FromSeconds(30)));

        Assert.Equal(1, winners.Count(task => task.Result));
        Assert.Equal(SessionState.Cancelled, session.State);
    }

    [Fact]
    public void AdoptAuthoritativeCwd_ReportsWhetherLocalCopyActuallyChanged()
    {
        var session = new Session("s1", @"C:\repo\demo");

        Assert.False(session.AdoptAuthoritativeCwd(@"C:\repo\demo"));
        Assert.True(session.AdoptAuthoritativeCwd(@"C:\repo\moved"));
        Assert.Equal(@"C:\repo\moved", session.Cwd);
        Assert.False(session.AdoptAuthoritativeCwd(@"C:\repo\moved"));
    }

    [Fact]
    public void RestoreSnapshot_RollsBackStateModeAndHistoryTogether()
    {
        var session = new Session("s1", @"C:\repo\demo");
        session.AppendHistory(SessionUpdateEntry.CreateTextMessage("kept"));
        var history = session.SnapshotHistory();
        var mode = new SessionModeState
        {
            CurrentModeId = "chat",
            AvailableModes = [new SessionMode("chat", "Chat")]
        };

        session.AppendHistory(SessionUpdateEntry.CreateTextMessage("discarded"));
        session.SetState(SessionState.Error);
        session.RestoreSnapshot(SessionState.Active, mode, history);

        Assert.Equal(SessionState.Active, session.State);
        Assert.Equal("chat", session.SnapshotMode().CurrentModeId);
        Assert.Equal(["kept"], session.SnapshotHistory().Select(entry => entry.TextContent));
    }

    [Fact]
    public void ResetForNewSession_ClearsHistoryAndReactivates()
    {
        var session = new Session("s1", @"C:\repo\demo");
        session.AppendHistory(SessionUpdateEntry.CreateTextMessage("stale"));
        session.SetState(SessionState.Cancelled);

        session.ResetForNewSession();

        Assert.Equal(SessionState.Active, session.State);
        Assert.Empty(session.SnapshotHistory());
    }
}

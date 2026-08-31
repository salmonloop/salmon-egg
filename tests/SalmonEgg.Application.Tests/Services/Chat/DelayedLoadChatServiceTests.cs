using System;
using Moq;
using SalmonEgg.Application.Services.Chat;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class DelayedLoadChatServiceTests
{
    [Fact]
    public void Dispose_ForwardsToInnerService()
    {
        var inner = new Mock<IChatService>(MockBehavior.Loose);
        var sut = new DelayedLoadChatService(inner.Object, TimeSpan.FromMilliseconds(1));

        sut.Dispose();

        // 装饰器不转发 Dispose 时,被包装的 ChatService 的 ACP 事件订阅会永久泄漏。
        inner.Verify(service => service.Dispose(), Times.Once);
    }
}

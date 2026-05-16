using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.Tests.Services.Chat;

public class DelayedLoadChatServiceTests
{
    private readonly Mock<IChatService> _innerMock;
    private readonly TimeSpan _delay;

    public DelayedLoadChatServiceTests()
    {
        _innerMock = new Mock<IChatService>(MockBehavior.Strict);
        _delay = TimeSpan.FromMilliseconds(50);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenInnerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new DelayedLoadChatService(null!, _delay));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenDelayIsZeroOrNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelayedLoadChatService(_innerMock.Object, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelayedLoadChatService(_innerMock.Object, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task LoadSessionAsync_DelaysAndCallsInner()
    {
        // Arrange
        var service = new DelayedLoadChatService(_innerMock.Object, _delay);
        var loadParams = new SessionLoadParams("test-session", "/path/to/cwd", null);
        var expectedResponse = new SessionLoadResponse();

        _innerMock.Setup(x => x.LoadSessionAsync(loadParams, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await service.LoadSessionAsync(loadParams);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(elapsed >= _delay || elapsed >= _delay.Subtract(TimeSpan.FromMilliseconds(15)));
        Assert.Same(expectedResponse, result);
        _innerMock.Verify(x => x.LoadSessionAsync(loadParams, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResumeSessionAsync_DelaysAndCallsInner()
    {
        // Arrange
        var service = new DelayedLoadChatService(_innerMock.Object, _delay);
        var resumeParams = new SessionResumeParams("test-session", "/path/to/cwd", null);
        var expectedResponse = new SessionResumeResponse();

        _innerMock.Setup(x => x.ResumeSessionAsync(resumeParams, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await service.ResumeSessionAsync(resumeParams);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(elapsed >= _delay || elapsed >= _delay.Subtract(TimeSpan.FromMilliseconds(15)));
        Assert.Same(expectedResponse, result);
        _innerMock.Verify(x => x.ResumeSessionAsync(resumeParams, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadSessionAsync_ThrowsTaskCanceledException_WhenCancelledDuringDelay()
    {
        // Arrange
        var service = new DelayedLoadChatService(_innerMock.Object, TimeSpan.FromSeconds(5));
        var loadParams = new SessionLoadParams("test-session", "/path/to/cwd", null);
        var cts = new CancellationTokenSource();

        // Act
        var task = service.LoadSessionAsync(loadParams, cts.Token);
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        _innerMock.Verify(x => x.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Properties_ForwardToInner()
    {
        // Arrange
        _innerMock.SetupGet(x => x.CurrentSessionId).Returns("test-session");
        _innerMock.SetupGet(x => x.IsInitialized).Returns(true);
        _innerMock.SetupGet(x => x.IsConnected).Returns(true);

        var service = new DelayedLoadChatService(_innerMock.Object, _delay);

        // Act & Assert
        Assert.Equal("test-session", service.CurrentSessionId);
        Assert.True(service.IsInitialized);
        Assert.True(service.IsConnected);

        _innerMock.VerifyGet(x => x.CurrentSessionId, Times.Once);
        _innerMock.VerifyGet(x => x.IsInitialized, Times.Once);
        _innerMock.VerifyGet(x => x.IsConnected, Times.Once);
    }
}

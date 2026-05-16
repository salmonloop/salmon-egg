using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Application.Tests.Services.Chat;

public class DelayedLoadChatServiceTests
{
    private readonly Mock<IChatService> _innerMock;
    private readonly TimeSpan _delay;
    private readonly DelayedLoadChatService _service;

    public DelayedLoadChatServiceTests()
    {
        _innerMock = new Mock<IChatService>();
        _delay = TimeSpan.FromMilliseconds(50);
        _service = new DelayedLoadChatService(_innerMock.Object, _delay);
    }

    [Fact]
    public void Constructor_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DelayedLoadChatService(null!, _delay));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Constructor_InvalidDelay_ThrowsArgumentOutOfRangeException(int delayMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelayedLoadChatService(_innerMock.Object, TimeSpan.FromMilliseconds(delayMs)));
    }

    [Fact]
    public void Properties_AreDelegatedToInner()
    {
        // Arrange
        _innerMock.Setup(x => x.CurrentSessionId).Returns("session-123");
        _innerMock.Setup(x => x.IsInitialized).Returns(true);
        _innerMock.Setup(x => x.IsConnected).Returns(true);

        // Act & Assert
        Assert.Equal("session-123", _service.CurrentSessionId);
        Assert.True(_service.IsInitialized);
        Assert.True(_service.IsConnected);

        _innerMock.Verify(x => x.CurrentSessionId, Times.Once);
        _innerMock.Verify(x => x.IsInitialized, Times.Once);
        _innerMock.Verify(x => x.IsConnected, Times.Once);
    }

    [Fact]
    public async Task LoadSessionAsync_WithToken_IntroducesDelay()
    {
        // Arrange
        var parameters = new SessionLoadParams();
        var response = new SessionLoadResponse();
        _innerMock.Setup(x => x.LoadSessionAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _service.LoadSessionAsync(parameters, CancellationToken.None);

        stopwatch.Stop();

        // Assert
        Assert.Same(response, result);
        // Allow a small tolerance for Task.Delay timer resolution to prevent flaky tests in CI
        Assert.True(stopwatch.Elapsed >= _delay - TimeSpan.FromMilliseconds(15));
        _innerMock.Verify(x => x.LoadSessionAsync(parameters, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ResumeSessionAsync_WithToken_IntroducesDelay()
    {
        // Arrange
        var parameters = new SessionResumeParams();
        var response = new SessionResumeResponse();
        _innerMock.Setup(x => x.ResumeSessionAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _service.ResumeSessionAsync(parameters, CancellationToken.None);

        stopwatch.Stop();

        // Assert
        Assert.Same(response, result);
        Assert.True(stopwatch.Elapsed >= _delay - TimeSpan.FromMilliseconds(15));
        _innerMock.Verify(x => x.ResumeSessionAsync(parameters, CancellationToken.None), Times.Once);
    }

    [Fact]
    public void Events_AreDelegatedToInner()
    {
        // Arrange
        int invokeCount = 0;
        EventHandler<string> handler = (sender, e) => invokeCount++;

        // Act - Subscription
        _service.ErrorOccurred += handler;
        _innerMock.Raise(x => x.ErrorOccurred += null, this, "test error");

        // Act - Unsubscription
        _service.ErrorOccurred -= handler;
        _innerMock.Raise(x => x.ErrorOccurred += null, this, "test error 2");

        // Assert
        Assert.Equal(1, invokeCount);
    }

    [Fact]
    public async Task DisconnectAsync_IsDelegatedToInner()
    {
        // Arrange
        _innerMock.Setup(x => x.DisconnectAsync()).ReturnsAsync(true);

        // Act
        var result = await _service.DisconnectAsync();

        // Assert
        Assert.True(result);
        _innerMock.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public void ClearHistory_IsDelegatedToInner()
    {
        // Act
        _service.ClearHistory();

        // Assert
        _innerMock.Verify(x => x.ClearHistory(), Times.Once);
    }
}

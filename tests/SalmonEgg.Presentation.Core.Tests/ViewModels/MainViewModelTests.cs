using System;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly Mock<IConnectionService> _mockConnectionService;
    private readonly Mock<IMessageService> _mockMessageService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<ILogger<MainViewModel>> _mockLogger;

    public MainViewModelTests()
    {
        _mockConnectionService = new Mock<IConnectionService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockConfigService = new Mock<IConfigurationService>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockLogger = new Mock<ILogger<MainViewModel>>();

        _mockConnectionService.Setup(x => x.ConnectionStateChanges)
            .Returns(new Subject<ConnectionState>());
        _mockMessageService.Setup(x => x.Notifications)
            .Returns(new Subject<AcpMessage>());
    }

    private MainViewModel CreateViewModel()
    {
        return new MainViewModel(
            _mockConnectionService.Object,
            _mockMessageService.Object,
            _mockConfigService.Object,
            _mockUiDispatcher.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Dispose_ShouldDisposeSubscriptions()
    {
        // Arrange
        var connectionStateSubject = new Subject<ConnectionState>();
        var notificationsSubject = new Subject<AcpMessage>();

        _mockConnectionService.Setup(x => x.ConnectionStateChanges).Returns(connectionStateSubject);
        _mockMessageService.Setup(x => x.Notifications).Returns(notificationsSubject);

        var viewModel = CreateViewModel();

        // Ensure subjects have observers before dispose
        Assert.True(connectionStateSubject.HasObservers);
        Assert.True(notificationsSubject.HasObservers);

        // Act
        viewModel.Dispose();

        // Assert
        Assert.False(connectionStateSubject.HasObservers, "Connection state subscription was not disposed.");
        Assert.False(notificationsSubject.HasObservers, "Notification subscription was not disposed.");
    }
}

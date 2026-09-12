using Moq;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task SessionSettings_OpenDialog_UsesLiveRowsAndKeepsEachEditUntilApplied()
    {
        // Arrange
        var ui = new Mock<IUiInteractionService>();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ChatViewModel? displayed = null;
        ui.Setup(value => value.ShowSessionSettingsAsync(It.IsAny<ChatViewModel>()))
            .Callback<ChatViewModel>(value => displayed = value)
            .Returns(closed.Task);
        await using var fixture = CreateViewModel(uiInteractionService: ui.Object,
            connectionSessionRegistry: new InMemoryAcpConnectionSessionRegistry());
        var service = CreateConnectedChatService();
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];

        // Act: opening and closing the modal do not submit or replace the existing row draft.
        var open = fixture.ViewModel.OpenSessionSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.Same(fixture.ViewModel, displayed);
        Assert.Same(row, Assert.Single(displayed!.ConfigOptions));
        Assert.True(fixture.ViewModel.OpenSessionSettingsCommand.CanExecute(null));
        closed.SetResult();
        await open;
        Assert.Equal("careful", row.SelectedOption?.Value);
        Assert.True(row.HasChanges);
        Assert.True(fixture.ViewModel.OpenSessionSettingsCommand.CanExecute(null));
        service.Verify(value => value.SetSessionConfigOptionAsync(It.IsAny<SalmonEgg.Acp.Protocol.SessionSetConfigOptionParams>()), Times.Never);
        ui.Verify(value => value.ShowSessionSettingsAsync(fixture.ViewModel), Times.Once);
    }

    [Fact]
    public async Task SessionSettings_NoAvailableConfiguration_DoesNotOpenDialog()
    {
        // Arrange
        var ui = new Mock<IUiInteractionService>();
        await using var fixture = CreateViewModel(uiInteractionService: ui.Object);

        // Act
        await fixture.ViewModel.OpenSessionSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.False(fixture.ViewModel.OpenSessionSettingsCommand.CanExecute(null));
        ui.Verify(value => value.ShowSessionSettingsAsync(It.IsAny<ChatViewModel>()), Times.Never);
    }
}

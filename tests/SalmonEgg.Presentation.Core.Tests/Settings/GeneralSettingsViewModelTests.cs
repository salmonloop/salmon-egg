using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class GeneralSettingsViewModelTests
{
    [Fact]
    public async Task ClearCacheAsync_WhenConfirmed_UsesLocalizedDialogCopyAndSuccessMessage()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        maintenance.Setup(m => m.ClearCacheAsync()).Returns(Task.CompletedTask);

        string? title = null;
        string? message = null;
        string? primary = null;
        string? close = null;
        string? info = null;

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(u => u.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback((string t, string m, string p, string c) =>
            {
                title = t;
                message = m;
                primary = p;
                close = c;
            })
            .ReturnsAsync(true);
        ui.Setup(u => u.ShowInfoAsync(It.IsAny<string>()))
            .Callback((string value) => info = value)
            .Returns(Task.CompletedTask);

        var localizer = new TestCoreStringLocalizer();
        var viewModel = new GeneralSettingsViewModel(
            CreatePreferences(),
            maintenance.Object,
            ui.Object,
            localizer,
            Mock.Of<ILogger<GeneralSettingsViewModel>>());

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal(localizer["General_ClearCacheTitle"], title);
        Assert.Equal(localizer["General_ClearCacheMessage"], message);
        Assert.Equal(localizer["General_ClearCachePrimary"], primary);
        Assert.Equal(localizer["Common_Cancel"], close);
        Assert.Equal(localizer["General_ClearCacheSuccess"], info);
        maintenance.Verify(m => m.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearCacheAsync_WhenMaintenanceFails_UsesLocalizedErrorMessage()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        maintenance.Setup(m => m.ClearCacheAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        string? info = null;
        var ui = new Mock<IUiInteractionService>();
        ui.Setup(u => u.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);
        ui.Setup(u => u.ShowInfoAsync(It.IsAny<string>()))
            .Callback((string value) => info = value)
            .Returns(Task.CompletedTask);

        var localizer = new TestCoreStringLocalizer();
        var viewModel = new GeneralSettingsViewModel(
            CreatePreferences(),
            maintenance.Object,
            ui.Object,
            localizer,
            Mock.Of<ILogger<GeneralSettingsViewModel>>());

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal(localizer["General_ClearCacheFailed"], info);
    }

    private static AppPreferencesViewModel CreatePreferences()
        => (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
}

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class GeneralSettingsViewModelTests
{
    [Fact]
    public async Task ClearCacheAsync_WhenConfirmed_UsesEnglishDialogCopyAndSuccessMessage()
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

        var viewModel = new GeneralSettingsViewModel(
            CreatePreferences(),
            maintenance.Object,
            ui.Object,
            Mock.Of<ILogger<GeneralSettingsViewModel>>());

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal("Clear cache", title);
        Assert.Equal("This deletes all files in the local cache folder.", message);
        Assert.Equal("Clear", primary);
        Assert.Equal("Cancel", close);
        Assert.Equal("Local cache cleared.", info);
        maintenance.Verify(m => m.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearCacheAsync_WhenMaintenanceFails_UsesEnglishErrorMessage()
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

        var viewModel = new GeneralSettingsViewModel(
            CreatePreferences(),
            maintenance.Object,
            ui.Object,
            Mock.Of<ILogger<GeneralSettingsViewModel>>());

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal("Failed to clear cache. Please try again later.", info);
    }

    private static AppPreferencesViewModel CreatePreferences()
        => (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
}

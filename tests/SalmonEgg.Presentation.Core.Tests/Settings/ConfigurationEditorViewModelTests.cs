using FluentValidation;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class ConfigurationEditorViewModelTests
{
    [Fact]
    public void TransportOptions_Should_PresentStdioAsSubprocessTransport()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(validator, configurationService.Object, logger.Object);

        Assert.Equal("Stdio（子进程）", viewModel.TransportOptions[0].Name);
    }
}

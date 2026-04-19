using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public class AppPreferencesViewModelRaceTests
{
    [Fact]
    public async Task ScheduleSave_RaceConditionTest()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(async () => await Task.Delay(1));

        var exceptions = new List<Exception>();

        var logger = new Mock<ILogger<AppPreferencesViewModel>>();
        logger.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
        .Callback(new InvocationAction(invocation =>
        {
            var exception = (Exception)invocation.Arguments[3];
            if (exception != null)
            {
                lock (exceptions)
                {
                    exceptions.Add(exception);
                }
            }
        }));

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        var task = Task.Run(async () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                vm.Theme = i.ToString();
                await Task.Delay(1);
            }
        });

        // Simulating the UI thread mutating the items and triggering events which call ScheduleSave internally
        for (int i = 0; i < 1000; i++)
        {
            vm.Projects.Add(new ProjectDefinition { ProjectId = i.ToString(), Name = i.ToString(), RootPath = i.ToString() });
            if (i % 5 == 0)
            {
                vm.Projects.RemoveAt(0);
            }
        }

        await task;
        await Task.Delay(100); // give time for the saves to settle

        // Assert no exceptions thrown during save (e.g. InvalidOperationException: Collection was modified, or ObjectDisposedException)
        Assert.Empty(exceptions);
    }
}

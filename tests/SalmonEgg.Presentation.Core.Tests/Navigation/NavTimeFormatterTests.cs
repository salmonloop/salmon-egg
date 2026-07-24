using System;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class NavTimeFormatterTests
{
    [Fact]
    public void ToRelativeText_WhenLocalizerReturnsNull_FallsBackWithoutThrowing()
    {
        var localizer = new Mock<IStringLocalizer<CoreStrings>>();
        localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns((LocalizedString?)null!);

        var text = NavTimeFormatter.ToRelativeText(
            DateTime.UtcNow.AddDays(-3),
            localizer.Object);

        Assert.Equal("3 d", text);
    }

    [Fact]
    public void ToRelativeText_WhenLocalizerIsNull_UsesFallback()
    {
        var text = NavTimeFormatter.ToRelativeText(
            DateTime.UtcNow.AddMinutes(-5),
            localizer: null);

        Assert.Equal("5 min", text);
    }
}

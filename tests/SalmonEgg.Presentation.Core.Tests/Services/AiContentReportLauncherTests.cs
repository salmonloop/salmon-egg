using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class AiContentReportLauncherTests
{
    [Fact]
    public void CanReport_WhenEmailMissing_ReturnsFalse()
    {
        var supportInfo = new Mock<IAppSupportInfoService>();
        supportInfo.SetupGet(service => service.ReportInappropriateAiContentEmail).Returns(string.Empty);

        var launcher = new AiContentReportLauncher(
            supportInfo.Object,
            Mock.Of<IPlatformShellService>(),
            new TestCoreStringLocalizer());

        Assert.False(launcher.CanReport);
    }

    [Fact]
    public async Task TryOpenReportAsync_BuildsSharedMailtoWithOptionalExcerpt()
    {
        var opened = new List<Uri>();
        var shell = new Mock<IPlatformShellService>();
        shell.Setup(service => service.OpenUriAsync(It.IsAny<Uri>()))
            .Returns<Uri>(uri =>
            {
                opened.Add(uri);
                return Task.FromResult(true);
            });

        var supportInfo = new Mock<IAppSupportInfoService>();
        supportInfo.SetupGet(service => service.ReportInappropriateAiContentEmail)
            .Returns("report@example.test");

        var launcher = new AiContentReportLauncher(
            supportInfo.Object,
            shell.Object,
            new TestCoreStringLocalizer());

        var success = await launcher.TryOpenReportAsync(
            appName: "SalmonEgg",
            appVersion: "1.2.3",
            protocolVersion: "1",
            contentExcerpt: "bad answer");

        Assert.True(success);
        var uri = Assert.Single(opened);
        Assert.Equal("mailto", uri.Scheme);
        Assert.Contains("report@example.test", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("subject=", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("body=", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("bad answer"), uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Related content:"), uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_WithoutEmail_ReturnsNull()
    {
        var uri = AiContentReportUriBuilder.TryCreate(
            email: " ",
            subject: "subject",
            appLabel: "App",
            appName: "SalmonEgg",
            versionLabel: "Version",
            appVersion: "1.0",
            protocolLabel: "Protocol",
            protocolVersion: "1",
            bodyPrompt: "describe");

        Assert.Null(uri);
    }

    [Fact]
    public void TryCreate_WithLongExcerpt_BoundsMailtoBodyWithoutSplittingTextElements()
    {
        var excerpt = string.Concat(Enumerable.Repeat("A", 1000)) + "\U0001F600tail";

        var uri = AiContentReportUriBuilder.TryCreate(
            email: "report@example.test",
            subject: "subject",
            appLabel: "App",
            appName: "SalmonEgg",
            versionLabel: "Version",
            appVersion: "1.0",
            protocolLabel: "Protocol",
            protocolVersion: "1",
            bodyPrompt: "describe",
            contentExcerptLabel: "Related content:",
            contentExcerpt: excerpt);

        Assert.NotNull(uri);
        var decoded = Uri.UnescapeDataString(uri.OriginalString);
        Assert.Contains(string.Concat(Enumerable.Repeat("A", 997)) + "...", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("\U0001F600", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("tail", decoded, StringComparison.Ordinal);
    }
}

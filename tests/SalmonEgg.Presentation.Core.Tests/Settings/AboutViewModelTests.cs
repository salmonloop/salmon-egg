using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class AboutViewModelTests
{
    [Fact]
    public async Task ReportInappropriateAiContentCommand_WhenSupportEmailConfigured_OpensMailtoUri()
    {
        var openedUris = new List<Uri>();
        var shell = new Mock<IPlatformShellService>();
        shell.Setup(service => service.OpenUriAsync(It.IsAny<Uri>()))
            .Returns<Uri>(uri =>
            {
                openedUris.Add(uri);
                return Task.FromResult(true);
            });

        var supportInfo = new Mock<IAppSupportInfoService>();
        supportInfo.SetupGet(service => service.ReportInappropriateAiContentEmail)
            .Returns("report@example.test");
        var launcher = new AiContentReportLauncher(
            supportInfo.Object,
            shell.Object,
            new TestCoreStringLocalizer());

        var viewModel = CreateViewModel(
            Mock.Of<IOpenSourceAcknowledgementsProvider>(),
            shell: shell.Object,
            aiContentReportLauncher: launcher);

        Assert.True(viewModel.CanReportInappropriateAiContent);

        await viewModel.ReportInappropriateAiContentCommand.ExecuteAsync(null);

        var opened = Assert.Single(openedUris);
        Assert.Equal("mailto", opened.Scheme);
        Assert.Contains("report@example.test", opened.OriginalString, StringComparison.Ordinal);
        Assert.Contains("subject=", opened.OriginalString, StringComparison.Ordinal);
        Assert.Contains("body=", opened.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void CanReportInappropriateAiContent_WhenSupportEmailMissing_ReturnsFalse()
    {
        var supportInfo = new Mock<IAppSupportInfoService>();
        supportInfo.SetupGet(service => service.ReportInappropriateAiContentEmail)
            .Returns(string.Empty);
        var launcher = new AiContentReportLauncher(
            supportInfo.Object,
            Mock.Of<IPlatformShellService>(),
            new TestCoreStringLocalizer());

        var viewModel = CreateViewModel(
            Mock.Of<IOpenSourceAcknowledgementsProvider>(),
            aiContentReportLauncher: launcher);

        Assert.False(viewModel.CanReportInappropriateAiContent);
    }

    [Fact]
    public async Task ReportInappropriateAiContentCommand_WhenEmailAppCannotOpen_ShowsReportSpecificFailure()
    {
        var localizer = new TestCoreStringLocalizer();
        var launcher = new Mock<IAiContentReportLauncher>();
        launcher.SetupGet(service => service.CanReport).Returns(true);
        launcher.Setup(service => service.TryOpenReportAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);
        var ui = new Mock<IUiInteractionService>();
        ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var viewModel = CreateViewModel(
            Mock.Of<IOpenSourceAcknowledgementsProvider>(),
            localizer: localizer,
            aiContentReportLauncher: launcher.Object,
            ui: ui.Object);

        await viewModel.ReportInappropriateAiContentCommand.ExecuteAsync(null);

        ui.Verify(service => service.ShowInfoAsync(localizer["AiContentReport_OpenFailed"]), Times.Once);
    }

    [Fact]
    public void Constructor_ProjectsOpenSourceAcknowledgementsWithLocalizedFallbacks()
    {
        var acknowledgements = new Mock<IOpenSourceAcknowledgementsProvider>();
        acknowledgements
            .Setup(provider => provider.GetAcknowledgements())
            .Returns(new[]
            {
                new OpenSourceAcknowledgement("Beta.Package", string.Empty, string.Empty, string.Empty),
                new OpenSourceAcknowledgement("Alpha.Package", "1.2.3", "MIT", "https://example.test/alpha")
            });

        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(acknowledgements.Object, localizer: localizer);

        Assert.Collection(
            viewModel.OpenSourceAcknowledgements,
            first =>
            {
                Assert.Equal("Alpha.Package", first.Name);
                Assert.Equal("1.2.3", first.Version);
                Assert.Equal("MIT", first.License);
                Assert.Equal("https://example.test/alpha", first.SourceUrl);
            },
            second =>
            {
                Assert.Equal("Beta.Package", second.Name);
                Assert.Equal(localizer["About_AcknowledgementVersionFallback"], second.Version);
                Assert.Equal(localizer["About_AcknowledgementLicenseFallback"], second.License);
                Assert.Equal(localizer["About_AcknowledgementSourceFallback"], second.SourceUrl);
            });
    }

    [Fact]
    public void OpenSourceAcknowledgements_ReevaluatesLocalizedFallbacks()
    {
        var acknowledgements = new Mock<IOpenSourceAcknowledgementsProvider>();
        acknowledgements
            .Setup(provider => provider.GetAcknowledgements())
            .Returns(new[]
            {
                new OpenSourceAcknowledgement("Beta.Package", string.Empty, string.Empty, string.Empty)
            });

        var localizer = new MutableFallbackLocalizer
        {
            VersionFallback = "version-a",
            LicenseFallback = "license-a",
            SourceFallback = "source-a"
        };
        var viewModel = CreateViewModel(acknowledgements.Object, localizer);

        var initial = Assert.Single(viewModel.OpenSourceAcknowledgements);
        Assert.Equal("version-a", initial.Version);
        Assert.Equal("license-a", initial.License);
        Assert.Equal("source-a", initial.SourceUrl);

        localizer.VersionFallback = "version-b";
        localizer.LicenseFallback = "license-b";
        localizer.SourceFallback = "source-b";

        var updated = Assert.Single(viewModel.OpenSourceAcknowledgements);
        Assert.Equal("version-b", updated.Version);
        Assert.Equal("license-b", updated.License);
        Assert.Equal("source-b", updated.SourceUrl);
    }

    private static AboutViewModel CreateViewModel(
        IOpenSourceAcknowledgementsProvider acknowledgements,
        IStringLocalizer<CoreStrings>? localizer = null,
        IPlatformShellService? shell = null,
        IAiContentReportLauncher? aiContentReportLauncher = null,
        IUiInteractionService? ui = null)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(service => service.SupportsExternalFileOpen).Returns(true);

        var documents = new Mock<IAppDocumentService>();
        documents.SetupGet(service => service.DocsRootPath).Returns("C:/app/docs");

        var effectiveLocalizer = localizer ?? new TestCoreStringLocalizer();
        var effectiveShell = shell ?? Mock.Of<IPlatformShellService>();
        if (aiContentReportLauncher is null)
        {
            var defaultSupportInfo = new Mock<IAppSupportInfoService>();
            defaultSupportInfo.SetupGet(service => service.ReportInappropriateAiContentEmail)
                .Returns("report@example.test");
            aiContentReportLauncher = new AiContentReportLauncher(
                defaultSupportInfo.Object,
                effectiveShell,
                effectiveLocalizer);
        }

        return new AboutViewModel(
            effectiveShell,
            aiContentReportLauncher,
            capabilities.Object,
            Mock.Of<IStorageLocationService>(),
            Mock.Of<IAppDataService>(),
            documents.Object,
            ui ?? Mock.Of<IUiInteractionService>(),
            effectiveLocalizer,
            acknowledgements);
    }

    private sealed class MutableFallbackLocalizer : IStringLocalizer<CoreStrings>
    {
        public string VersionFallback { get; set; } = string.Empty;

        public string LicenseFallback { get; set; } = string.Empty;

        public string SourceFallback { get; set; } = string.Empty;

        public LocalizedString this[string name] => new(name, Resolve(name));

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(CultureInfo.InvariantCulture, Resolve(name), arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public IStringLocalizer WithCulture(CultureInfo culture) => this;

        private string Resolve(string name)
            => name switch
            {
                "About_AcknowledgementVersionFallback" => VersionFallback,
                "About_AcknowledgementLicenseFallback" => LicenseFallback,
                "About_AcknowledgementSourceFallback" => SourceFallback,
                _ => name
            };
    }
}

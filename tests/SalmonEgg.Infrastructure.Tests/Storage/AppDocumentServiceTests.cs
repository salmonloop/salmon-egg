using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public class AppDocumentServiceTests
{
    [Fact]
    public void Constructor_NullPaths_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AppDocumentService(null!));
    }

    [Fact]
    public void DocsRootPath_ReturnsExpectedPath()
    {
        var mockAppData = new Mock<IAppDataService>();
        mockAppData.Setup(x => x.AppDataRootPath).Returns("/TestPath");
        var service = new AppDocumentService(mockAppData.Object);

        var result = service.DocsRootPath;

        Assert.Equal(Path.Combine("/TestPath", "docs"), result);
    }

    [Fact]
    public void GetPrivacyPolicyPath_ReturnsExpectedPath()
    {
        var mockAppData = new Mock<IAppDataService>();
        mockAppData.Setup(x => x.AppDataRootPath).Returns("/TestPath");
        var service = new AppDocumentService(mockAppData.Object);

        var result = service.GetPrivacyPolicyPath();

        Assert.Equal(Path.Combine("/TestPath", "docs", "privacy-policy.md"), result);
    }

    [Fact]
    public void GetReleaseNotesPath_ReturnsExpectedPath()
    {
        var mockAppData = new Mock<IAppDataService>();
        mockAppData.Setup(x => x.AppDataRootPath).Returns("/TestPath");
        var service = new AppDocumentService(mockAppData.Object);

        var result = service.GetReleaseNotesPath();

        Assert.Equal(Path.Combine("/TestPath", "docs", "release-notes.md"), result);
    }

    [Fact]
    public async Task ExistsAsync_ThrowsWhenCancelled()
    {
        var mockAppData = new Mock<IAppDataService>();
        var service = new AppDocumentService(mockAppData.Object);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExistsAsync("dummy.txt", cts.Token));
    }
}

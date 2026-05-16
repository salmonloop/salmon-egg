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
    private readonly Mock<IAppDataService> _mockAppDataService;
    private readonly AppDocumentService _service;
    private readonly string _mockAppDataRootPath = "/mock/app/data";

    public AppDocumentServiceTests()
    {
        _mockAppDataService = new Mock<IAppDataService>();
        _mockAppDataService.Setup(s => s.AppDataRootPath).Returns(_mockAppDataRootPath);
        _service = new AppDocumentService(_mockAppDataService.Object);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenPathsIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AppDocumentService(null!));
    }

    [Fact]
    public void DocsRootPath_ReturnsExpectedPath()
    {
        // Arrange
        var expectedPath = Path.Combine(_mockAppDataRootPath, "docs");

        // Act
        var result = _service.DocsRootPath;

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void GetPrivacyPolicyPath_ReturnsExpectedPath()
    {
        // Arrange
        var expectedPath = Path.Combine(_mockAppDataRootPath, "docs", "privacy-policy.md");

        // Act
        var result = _service.GetPrivacyPolicyPath();

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void GetReleaseNotesPath_ReturnsExpectedPath()
    {
        // Arrange
        var expectedPath = Path.Combine(_mockAppDataRootPath, "docs", "release-notes.md");

        // Act
        var result = _service.GetReleaseNotesPath();

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenFileExists()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            var result = await _service.ExistsAsync(tempFile);

            // Assert
            Assert.True(result);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".md");

        // Act
        var result = await _service.ExistsAsync(nonExistentFile);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ThrowsOperationCanceledException_WhenCancellationRequested()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ExistsAsync(tempFile, cts.Token));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

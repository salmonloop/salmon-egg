using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class LogFileCatalogTests : IDisposable
{
    private readonly string _root;

    public LogFileCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggLogFileCatalogTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task GetLatestAsync_MissingDirectory_ReturnsNull()
    {
        var sut = new LogFileCatalog();

        var latest = await sut.GetLatestAsync(Path.Combine(_root, "logs"));

        Assert.Null(latest);
    }

    [Fact]
    public async Task ReadTailAsync_ReturnsRequestedTail()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "app.log");
        await File.WriteAllTextAsync(path, "0123456789");
        var sut = new LogFileCatalog();

        var tail = await sut.ReadTailAsync(path, 4);

        Assert.Equal("6789", tail);
    }

    [Fact]
    public async Task ReadTailAsync_WhenLogFileIsOpenForWriting_StillReturnsTail()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "app.log");
        await File.WriteAllTextAsync(path, "0123456789");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        writer.Seek(0, SeekOrigin.End);
        await writer.WriteAsync("ABC"u8.ToArray());
        await writer.FlushAsync();
        var sut = new LogFileCatalog();

        var tail = await sut.ReadTailAsync(path, 4);

        Assert.Equal("9ABC", tail);
    }
}

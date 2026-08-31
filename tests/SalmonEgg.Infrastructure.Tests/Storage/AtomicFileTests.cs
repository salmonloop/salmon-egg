using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _root;

    public AtomicFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "salmon-egg-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public async Task WriteUtf8AtomicAsync_WritesContentToTargetPath()
    {
        var path = Path.Combine(_root, "target.txt");

        await AtomicFile.WriteUtf8AtomicAsync(path, "hello", TestContext.Current.CancellationToken);

        Assert.Equal("hello", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteUtf8AtomicAsync_OverwritesExistingFileAtomically()
    {
        var path = Path.Combine(_root, "target.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);

        await AtomicFile.WriteUtf8AtomicAsync(path, "new", TestContext.Current.CancellationToken);

        Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteUtf8AtomicAsync_LeavesNoTempArtifact()
    {
        var path = Path.Combine(_root, "target.txt");

        await AtomicFile.WriteUtf8AtomicAsync(path, "hello", TestContext.Current.CancellationToken);

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp.*"));
        Assert.Single(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public async Task WriteUtf8AtomicAsync_CreatesMissingParentDirectory()
    {
        var path = Path.Combine(_root, "nested", "deep", "target.txt");

        await AtomicFile.WriteUtf8AtomicAsync(path, "hello", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
        Assert.Equal("hello", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteUtf8AtomicAsync_WritesUtf8WithoutBom()
    {
        var path = Path.Combine(_root, "target.txt");

        await AtomicFile.WriteUtf8AtomicAsync(path, "hello", TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.NotEqual(0xEF, bytes.Length > 0 ? bytes[0] : 0);
        Assert.Equal("hello", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task WriteUtf8AtomicAsync_NullPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtomicFile.WriteUtf8AtomicAsync(null!, "content", TestContext.Current.CancellationToken));
    }
}

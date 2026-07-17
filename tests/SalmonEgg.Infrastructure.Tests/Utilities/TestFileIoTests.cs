using System.Text;
using SalmonEgg.TestSupport;

namespace SalmonEgg.Infrastructure.Tests.Utilities;

public sealed class TestFileIoTests : IDisposable
{
    private readonly string _tempRoot;

    public TestFileIoTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "salmonegg-file-io-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task WriteAllTextWithRetry_Succeeds_WhenAnExclusiveLockIsReleasedShortlyAfterward()
    {
        var path = Path.Combine(_tempRoot, "app.yaml");
        await File.WriteAllTextAsync(path, "before", Encoding.UTF8, TestContext.Current.CancellationToken);

        using var heldOpen = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(120, TestContext.Current.CancellationToken);
            heldOpen.Dispose();
        }, TestContext.Current.CancellationToken);

        TestFileIo.WriteAllTextWithRetry(path, "after", Encoding.UTF8, attempts: 6, delayMilliseconds: 50);
        await releaseTask;

        Assert.Equal("after", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void WriteAllTextWithRetry_WritesContent_WhenPathIsUnlocked()
    {
        var path = Path.Combine(_tempRoot, "unlocked.yaml");

        TestFileIo.WriteAllTextWithRetry(path, "ready", Encoding.UTF8);

        Assert.Equal("ready", File.ReadAllText(path, Encoding.UTF8));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}

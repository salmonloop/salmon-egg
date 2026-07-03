using System;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class VolatileSecureStorageTests
{
    [Fact]
    public async Task SaveThenLoad_WithinSameInstance_ReturnsValue()
    {
        var storage = new VolatileSecureStorage();

        await storage.SaveAsync("key", "secret");

        Assert.Equal("secret", await storage.LoadAsync("key"));
    }

    [Fact]
    public async Task Load_FromNewInstance_ReturnsNull()
    {
        var first = new VolatileSecureStorage();
        await first.SaveAsync("key", "secret");
        var second = new VolatileSecureStorage();

        Assert.Null(await second.LoadAsync("key"));
    }

    [Fact]
    public async Task NullOrEmptyKey_ThrowsArgumentNullException()
    {
        var storage = new VolatileSecureStorage();

        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.SaveAsync(string.Empty, "secret"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.LoadAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.DeleteAsync(string.Empty));
    }
}

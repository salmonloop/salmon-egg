using System;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class VolatileSecureStorageTests
{
    [Fact]
    public async Task SaveLoadDelete_RoundTripsWithinInstance()
    {
        var storage = new VolatileSecureStorage();

        await storage.SaveAsync("token", "secret-value");

        Assert.Equal("secret-value", await storage.LoadAsync("token"));

        await storage.DeleteAsync("token");

        Assert.Null(await storage.LoadAsync("token"));
    }

    [Fact]
    public async Task NewInstance_DoesNotPersistPreviousSecrets()
    {
        var first = new VolatileSecureStorage();
        await first.SaveAsync("token", "secret-value");

        var second = new VolatileSecureStorage();

        Assert.Null(await second.LoadAsync("token"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task KeyRequired(string? key)
    {
        var storage = new VolatileSecureStorage();

        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.SaveAsync(key!, "value"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.LoadAsync(key!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.DeleteAsync(key!));
    }
}

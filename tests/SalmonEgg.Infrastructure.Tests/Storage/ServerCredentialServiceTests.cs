using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ServerCredentialServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReportsTokenPresenceWithoutReturningItsValue()
    {
        var storage = new RecordingSecureStorage();
        await storage.SaveAsync("salmonegg/config/server-1/token", "token-value");
        var service = new ServerCredentialService(storage);

        var status = await service.GetStatusAsync("server-1");

        Assert.True(status.HasToken);
        Assert.False(status.HasApiKey);
        Assert.True(status.HasAny);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsApiKeyPresenceWithoutReturningItsValue()
    {
        var storage = new RecordingSecureStorage();
        await storage.SaveAsync("salmonegg/config/server-2/apiKey", "api-key-value");
        var service = new ServerCredentialService(storage);

        var status = await service.GetStatusAsync("server-2");

        Assert.False(status.HasToken);
        Assert.True(status.HasApiKey);
        Assert.True(status.HasAny);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsBothKindsWhenStorageContainsLegacyOrphanedKeys()
    {
        var storage = new RecordingSecureStorage();
        await storage.SaveAsync("salmonegg/config/server-3/token", "token-value");
        await storage.SaveAsync("salmonegg/config/server-3/apiKey", "api-key-value");
        var service = new ServerCredentialService(storage);

        var status = await service.GetStatusAsync("server-3");

        Assert.True(status.HasToken);
        Assert.True(status.HasApiKey);
        Assert.True(status.HasAny);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetStatusAsync_RejectsEmptyServerId(string serverId)
    {
        var service = new ServerCredentialService(new RecordingSecureStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStatusAsync(serverId));
    }

    private sealed class RecordingSecureStorage : ISecureStorage
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task SaveAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(string key)
        {
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

/// <summary>
/// The fallback store is weaker than the platform keychain, so falling back to it is a security
/// downgrade that the app otherwise survives silently. These tests pin that the downgrade both
/// preserves the secret and leaves a record.
/// </summary>
public sealed class FallbackSecureStorageTests
{
    [Fact]
    public async Task SaveAsync_WhenPlatformStoreIsUnavailable_WritesToFallbackAndWarns()
    {
        var primary = new UnavailableSecureStorage();
        var fallback = new RecordingSecureStorage();
        var logger = new RecordingLogger();
        var sut = new FallbackSecureStorage(primary, fallback, logger);

        await sut.SaveAsync("token", "secret-value");

        Assert.Equal("secret-value", Assert.Contains("token", fallback.Values));
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task SaveAsync_WhenPlatformStoreWorks_LeavesNoSecretInFallbackAndDoesNotWarn()
    {
        var primary = new RecordingSecureStorage();
        var fallback = new RecordingSecureStorage();
        var logger = new RecordingLogger();
        var sut = new FallbackSecureStorage(primary, fallback, logger);

        await sut.SaveAsync("token", "secret-value");

        Assert.Equal("secret-value", Assert.Contains("token", primary.Values));
        Assert.Empty(fallback.Values);
        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task SaveAsync_WhenPlatformStoreRecovers_RetiresTheEarlierDowngradedSecret()
    {
        // A secret downgraded while the platform store was unavailable must not outlive being
        // overwritten: if it stays in the weaker store, the next read that falls through — during the
        // same kind of outage that caused the downgrade — resurrects the superseded secret.
        var primary = new RecordingSecureStorage();
        var fallback = new RecordingSecureStorage();
        var sut = new FallbackSecureStorage(primary, fallback);
        fallback.Values["token"] = "superseded-value";

        await sut.SaveAsync("token", "rotated-value");

        Assert.Equal("rotated-value", Assert.Contains("token", primary.Values));
        Assert.Empty(fallback.Values);
    }

    [Fact]
    public async Task LoadAsync_WhenPlatformStoreIsUnavailable_ReadsFallbackAndWarns()
    {
        var primary = new UnavailableSecureStorage();
        var fallback = new RecordingSecureStorage();
        await fallback.SaveAsync("token", "downgraded-value");
        var logger = new RecordingLogger();
        var sut = new FallbackSecureStorage(primary, fallback, logger);

        var loaded = await sut.LoadAsync("token");

        Assert.Equal("downgraded-value", loaded);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task LoadAsync_WhenLegacyFallbackValueExists_TreatsItAsAuthoritativeAndWarns()
    {
        var primary = new RecordingSecureStorage();
        await primary.SaveAsync("token", "stale-platform-value");
        var fallback = new RecordingSecureStorage();
        await fallback.SaveAsync("token", "outage-value");
        var logger = new RecordingLogger();
        var sut = new FallbackSecureStorage(primary, fallback, logger);

        var loaded = await sut.LoadAsync("token");

        Assert.Equal("outage-value", loaded);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task SaveAsync_WhenPlatformStoreRecovers_DoesNotResurrectItsStaleValue()
    {
        var primary = new AvailabilityChangingSecureStorage();
        await primary.SaveAsync("token", "stale-platform-value");
        primary.IsAvailable = false;
        var fallback = new RecordingSecureStorage();
        var sut = new FallbackSecureStorage(primary, fallback, new RecordingLogger());

        await sut.SaveAsync("token", "outage-value");
        primary.IsAvailable = true;

        Assert.Equal("outage-value", await sut.LoadAsync("token"));
        Assert.Equal("stale-platform-value", Assert.Contains("token", primary.Values));
    }

    [Fact]
    public async Task DeleteAsync_WhenPlatformStoreRecovers_DoesNotResurrectItsStaleValue()
    {
        var primary = new AvailabilityChangingSecureStorage();
        await primary.SaveAsync("token", "stale-platform-value");
        primary.IsAvailable = false;
        var fallback = new RecordingSecureStorage();
        await fallback.SaveAsync("token", "outage-value");
        var sut = new FallbackSecureStorage(primary, fallback, new RecordingLogger());

        await sut.DeleteAsync("token");
        primary.IsAvailable = true;

        Assert.Null(await sut.LoadAsync("token"));
        Assert.Equal("stale-platform-value", Assert.Contains("token", primary.Values));
    }

    [Fact]
    public async Task DeleteAsync_WhenPlatformStoreIsUnavailable_StillClearsFallbackAndWarns()
    {
        // A secret downgraded earlier must not survive a delete.
        var primary = new UnavailableSecureStorage();
        var fallback = new RecordingSecureStorage();
        await fallback.SaveAsync("token", "downgraded-value");
        var logger = new RecordingLogger();
        var sut = new FallbackSecureStorage(primary, fallback, logger);

        await sut.DeleteAsync("token");

        Assert.DoesNotContain("token", fallback.Values);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task DeleteAsync_WhenPlatformStoreWorks_ClearsBothStores()
    {
        var primary = new RecordingSecureStorage();
        await primary.SaveAsync("token", "platform-value");
        var fallback = new RecordingSecureStorage();
        await fallback.SaveAsync("token", "downgraded-value");
        var sut = new FallbackSecureStorage(primary, fallback, new RecordingLogger());

        await sut.DeleteAsync("token");

        Assert.Empty(primary.Values);
        Assert.Empty(fallback.Values);
    }

    private sealed class RecordingSecureStorage : ISecureStorage
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public Task SaveAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
            => Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        public Task DeleteAsync(string key)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableSecureStorage : ISecureStorage
    {
        public Task SaveAsync(string key, string value)
            => throw new SecureStorageUnavailableException("platform store unavailable");

        public Task<string?> LoadAsync(string key)
            => throw new SecureStorageUnavailableException("platform store unavailable");

        public Task DeleteAsync(string key)
            => throw new SecureStorageUnavailableException("platform store unavailable");
    }

    private sealed class AvailabilityChangingSecureStorage : ISecureStorage
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool IsAvailable { get; set; } = true;

        public Task SaveAsync(string key, string value)
        {
            EnsureAvailable();
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            EnsureAvailable();
            return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
        }

        public Task DeleteAsync(string key)
        {
            EnsureAvailable();
            Values.Remove(key);
            return Task.CompletedTask;
        }

        private void EnsureAvailable()
        {
            if (!IsAvailable)
            {
                throw new SecureStorageUnavailableException("platform store unavailable");
            }
        }
    }

    private sealed class RecordingLogger : ILogger<FallbackSecureStorage>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConfigurationRecoveryCoordinatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appData;
    private readonly FileSystemAppFileStore _fileStore;
    private readonly MemorySecureStorage _secureStorage;
    private readonly ConfigurationRecoveryCoordinator _coordinator;

    public ConfigurationRecoveryCoordinatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggRecoveryTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _testDirectory, EnvironmentVariableTarget.Process);
        _appData = new AppDataService();
        _fileStore = new FileSystemAppFileStore();
        _secureStorage = new MemorySecureStorage();
        var lockProvider = new ConfigurationProfileLockProvider(_appData);
        _coordinator = new ConfigurationRecoveryCoordinator(_fileStore, _secureStorage, _appData, lockProvider);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoverPendingTransactionsAsync_PreparingJournal_CleansPreparationWithoutChangingPrimaryState()
    {
        const string profileId = "preparing-profile";
        var serverPath = GetServerPath(profileId);
        await _fileStore.WriteAllTextAsync(serverPath, "current-yaml", TestContext.Current.CancellationToken);
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(profileId), "current-token");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), "partial-backup");
        await WriteJournalAsync(CreateJournal(profileId, serverPath, ConfigurationRecoveryJournalState.Preparing));

        await _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("current-yaml", await _fileStore.ReadAllTextAsync(serverPath, TestContext.Current.CancellationToken));
        Assert.Equal("current-token", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(profileId)));
        Assert.Null(await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)));
        Assert.False(File.Exists(_coordinator.GetJournalPath(profileId)));
    }

    [Theory]
    [InlineData((int)ConfigurationRecoveryJournalState.Ready)]
    [InlineData((int)ConfigurationRecoveryJournalState.YamlApplied)]
    public async Task RecoverPendingTransactionsAsync_UncommittedJournal_RestoresYamlAndSecrets(
        int stateValue)
    {
        var state = (ConfigurationRecoveryJournalState)stateValue;
        const string profileId = "uncommitted-profile";
        var serverPath = GetServerPath(profileId);
        await _fileStore.WriteAllTextAsync(serverPath, "new-yaml", TestContext.Current.CancellationToken);
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(profileId), "new-token");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), "old-token");
        await WriteJournalAsync(CreateJournal(profileId, serverPath, state));

        await _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("old-yaml", await _fileStore.ReadAllTextAsync(serverPath, TestContext.Current.CancellationToken));
        Assert.Equal("old-token", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(profileId)));
        Assert.Null(await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)));
        Assert.False(File.Exists(_coordinator.GetJournalPath(profileId)));
    }

    [Fact]
    public async Task RecoverPendingTransactionsAsync_CommittedJournal_KeepsPrimaryStateAndCleansRecoveryMaterial()
    {
        const string profileId = "committed-profile";
        var serverPath = GetServerPath(profileId);
        await _fileStore.WriteAllTextAsync(serverPath, "new-yaml", TestContext.Current.CancellationToken);
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(profileId), "new-token");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), "old-token");
        await WriteJournalAsync(CreateJournal(profileId, serverPath, ConfigurationRecoveryJournalState.Committed));

        await _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("new-yaml", await _fileStore.ReadAllTextAsync(serverPath, TestContext.Current.CancellationToken));
        Assert.Equal("new-token", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(profileId)));
        Assert.Null(await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)));
        Assert.False(File.Exists(_coordinator.GetJournalPath(profileId)));
    }

    [Fact]
    public async Task RecoverPendingTransactionsAsync_UncommittedJournal_CleansOrphanedFileTransactionArtifacts()
    {
        const string profileId = "orphan-artifacts-profile";
        var serverPath = GetServerPath(profileId);
        await _fileStore.WriteAllTextAsync(serverPath, "new-yaml", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            serverPath + ConfigurationFileTransactionArtifacts.PendingSuffix + "crash",
            "candidate",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            serverPath + ConfigurationFileTransactionArtifacts.RollbackSuffix + "crash",
            "previous",
            TestContext.Current.CancellationToken);
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), "old-token");
        await WriteJournalAsync(CreateJournal(profileId, serverPath, ConfigurationRecoveryJournalState.YamlApplied));

        await _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(serverPath + ConfigurationFileTransactionArtifacts.PendingSuffix + "crash"));
        Assert.False(File.Exists(serverPath + ConfigurationFileTransactionArtifacts.RollbackSuffix + "crash"));
    }

    [Fact]
    public async Task RecoverPendingTransactionsAsync_JournalOutsideProfilePath_RequiresRecovery()
    {
        const string profileId = "path-validation-profile";
        var journalPath = _coordinator.GetJournalPath(profileId);
        var journal = CreateJournal(profileId, Path.Combine(_testDirectory, "outside.yaml"), ConfigurationRecoveryJournalState.Ready);
        await WriteJournalAsync(journal);

        var exception = await Assert.ThrowsAsync<ConfigurationRecoveryRequiredException>(
            () => _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(journalPath));
    }

    [Fact]
    public async Task RecoverPendingTransactionsAsync_CleanupFailsAfterRollback_RetryOnlyCleansMaterial()
    {
        const string profileId = "rolled-back-cleanup-profile";
        var serverPath = GetServerPath(profileId);
        await _fileStore.WriteAllTextAsync(serverPath, "new-yaml", TestContext.Current.CancellationToken);
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(profileId), "new-token");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetApiKeyKey(profileId), "new-api-key");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), "old-token");
        await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId), "old-api-key");
        await WriteJournalAsync(CreateJournal(
            profileId,
            serverPath,
            ConfigurationRecoveryJournalState.YamlApplied) with
        {
            OldApiKeyPresent = true
        });
        _secureStorage.FailDeleteOnceForKey = ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId);

        await Assert.ThrowsAsync<ConfigurationRecoveryRequiredException>(
            () => _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken));

        var journalJson = await File.ReadAllTextAsync(
            _coordinator.GetJournalPath(profileId),
            TestContext.Current.CancellationToken);
        var journal = JsonSerializer.Deserialize(
            journalJson,
            ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryJournal);
        Assert.NotNull(journal);
        Assert.Equal(ConfigurationRecoveryJournalState.RolledBack, journal!.State);
        Assert.Null(await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)));

        await _coordinator.RecoverPendingTransactionsAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_coordinator.GetJournalPath(profileId)));
        Assert.Equal("old-yaml", await _fileStore.ReadAllTextAsync(serverPath, TestContext.Current.CancellationToken));
        Assert.Equal("old-token", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(profileId)));
        Assert.Equal("old-api-key", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(profileId)));
    }

    [Fact]
    public async Task PrepareAsync_JournalDoesNotContainSecretValues()
    {
        const string profileId = "secret-boundary";
        var serverPath = GetServerPath(profileId);

        await _coordinator.PrepareAsync(
            profileId,
            serverPath,
            "old-yaml",
            oldFileExisted: true,
            "old-token-secret",
            "old-api-secret",
            TestContext.Current.CancellationToken);

        var journal = await File.ReadAllTextAsync(
            _coordinator.GetJournalPath(profileId),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("old-token-secret", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("old-api-secret", journal, StringComparison.Ordinal);
        Assert.Equal("old-token-secret", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)));
        Assert.Equal("old-api-secret", await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId)));
    }

    private ConfigurationRecoveryJournal CreateJournal(
        string profileId,
        string serverPath,
        ConfigurationRecoveryJournalState state) =>
        new(
            Version: 1,
            TransactionId: Guid.NewGuid().ToString("N"),
            ProfileId: profileId,
            ServerPath: serverPath,
            OldYaml: "old-yaml",
            OldFileExisted: true,
            OldTokenPresent: true,
            OldApiKeyPresent: false,
            State: state);

    private Task WriteJournalAsync(ConfigurationRecoveryJournal journal)
    {
        var json = JsonSerializer.Serialize(
            journal,
            ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryJournal);
        return _fileStore.WriteAllTextAsync(
            _coordinator.GetJournalPath(journal.ProfileId),
            json,
            TestContext.Current.CancellationToken);
    }

    private string GetServerPath(string profileId)
        => Path.Combine(_appData.ConfigRootPath, "servers", profileId + ".yaml");

    private sealed class MemorySecureStorage : ISecureStorage
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? FailDeleteOnceForKey { get; set; }

        public Task SaveAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(string key)
        {
            if (string.Equals(FailDeleteOnceForKey, key, StringComparison.Ordinal))
            {
                FailDeleteOnceForKey = null;
                throw new IOException("injected recovery cleanup failure");
            }

            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}

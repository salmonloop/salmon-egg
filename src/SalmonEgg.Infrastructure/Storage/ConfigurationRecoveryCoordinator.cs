using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

internal sealed class ConfigurationRecoveryCoordinator
{
    private const int CurrentJournalVersion = 1;
    private const string JournalSuffix = ".journal.json";

    private readonly IConfigurationFileStore _fileStore;
    private readonly ISecureStorage _secureStorage;
    private readonly ISecureStorage _recoveryMaterialStorage;
    private readonly ConfigurationProfileLockProvider _lockProvider;
    private readonly string _recoveryDirectory;
    private readonly string _serversDirectory;

    public ConfigurationRecoveryCoordinator(
        IConfigurationFileStore fileStore,
        ISecureStorage secureStorage,
        IAppDataService appData,
        ConfigurationProfileLockProvider lockProvider)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        // Rollback material is a copy of a secret the caller already stored, so it must not be blocked by
        // a fail-closed policy: if it were, clearing or deleting a previously downgraded secret would be
        // impossible. See ISecureStorageRecoveryMaterialSource.
        _recoveryMaterialStorage = secureStorage is ISecureStorageRecoveryMaterialSource recoveryMaterialSource
            ? recoveryMaterialSource.GetRecoveryMaterialStore()
            : _secureStorage;
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _recoveryDirectory = Path.Combine(appData.ConfigRootPath, "recovery");
        _serversDirectory = Path.Combine(appData.ConfigRootPath, "servers");
    }

    public async Task RecoverPendingTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var journals = new List<string>();
            await foreach (var path in _fileStore.EnumerateFilesAsync(_recoveryDirectory, "*" + JournalSuffix, cancellationToken).ConfigureAwait(false))
            {
                journals.Add(path);
            }

            foreach (var journalPath in journals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = await ReadJournalAsync(journalPath, cancellationToken).ConfigureAwait(false)
                    ?? throw new ConfigurationRecoveryRequiredException(journalPath, "The recovery journal is missing.");
                ValidateJournal(document, journalPath);

                await using var profileLock = await _lockProvider.AcquireAsync(document.ProfileId, cancellationToken).ConfigureAwait(false);
                await RecoverProfileUnderLockAsync(document.ProfileId, document, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ConfigurationRecoveryRequiredException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ConfigurationRecoveryRequiredException(
                _recoveryDirectory,
                "Pending configuration recovery could not be inspected.",
                exception);
        }
    }

    public async Task RecoverProfileUnderLockAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var journalPath = GetJournalPath(profileId);
        var document = await ReadJournalAsync(journalPath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        ValidateJournal(document, journalPath);
        await RecoverProfileUnderLockAsync(profileId, document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfigurationRecoveryJournal> PrepareAsync(
        string profileId,
        string serverPath,
        string? oldYaml,
        bool oldFileExisted,
        string? oldToken,
        string? oldApiKey,
        CancellationToken cancellationToken = default)
    {
        var document = new ConfigurationRecoveryJournal(
            CurrentJournalVersion,
            Guid.NewGuid().ToString("N"),
            profileId,
            serverPath,
            oldYaml,
            oldFileExisted,
            oldToken is not null,
            oldApiKey is not null,
            ConfigurationRecoveryJournalState.Preparing);

        ValidateJournal(document, GetJournalPath(profileId));
        await WriteJournalAsync(document, cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteBackupAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId), oldToken, cancellationToken).ConfigureAwait(false);
            await WriteBackupAsync(ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId), oldApiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new ConfigurationRecoverySecretException(exception);
        }

        document = document with { State = ConfigurationRecoveryJournalState.Ready };
        await WriteJournalAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public Task MarkYamlAppliedAsync(ConfigurationRecoveryJournal document, CancellationToken cancellationToken = default) =>
        WriteJournalAsync(document with { State = ConfigurationRecoveryJournalState.YamlApplied }, cancellationToken);

    public async Task<ConfigurationRecoveryJournal> MarkCommittedAsync(
        ConfigurationRecoveryJournal document,
        CancellationToken cancellationToken = default)
    {
        var committed = document with { State = ConfigurationRecoveryJournalState.Committed };
        await WriteJournalAsync(committed, cancellationToken).ConfigureAwait(false);
        return committed;
    }

    public async Task CleanupCommittedBestEffortAsync(
        ConfigurationRecoveryJournal document,
        CancellationToken cancellationToken = default)
    {
        // The committed marker remains present until the file transaction has discarded its own
        // rollback material. A cleanup failure must not turn an already committed mutation into a
        // false command failure; startup recovery retries while the marker remains.
        try
        {
            await CleanupRecoveryMaterialAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the committed journal for the next startup.
        }
    }

    public string GetJournalPath(string profileId)
    {
        var fileName = GetSafeFileName(profileId);
        return Path.Combine(_recoveryDirectory, fileName + JournalSuffix);
    }

    private async Task RecoverProfileUnderLockAsync(
        string profileId,
        ConfigurationRecoveryJournal document,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(document.ProfileId, profileId, StringComparison.Ordinal))
        {
            throw new ConfigurationRecoveryRequiredException(
                GetJournalPath(profileId),
                "The recovery journal profile identity does not match its owner.");
        }

        if (document.State == ConfigurationRecoveryJournalState.Preparing)
        {
            try
            {
                await CleanupRecoveryMaterialAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new ConfigurationRecoveryRequiredException(
                    GetJournalPath(profileId),
                    "The incomplete configuration preparation could not be cleaned up.",
                    exception);
            }

            return;
        }

        if (document.State is ConfigurationRecoveryJournalState.Committed or ConfigurationRecoveryJournalState.RolledBack)
        {
            try
            {
                await CleanupRecoveryMaterialAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new ConfigurationRecoveryRequiredException(
                    GetJournalPath(profileId),
                    "The completed configuration recovery material could not be cleaned up.",
                    exception);
            }

            return;
        }

        var tokenBackup = document.OldTokenPresent
            ? await _recoveryMaterialStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)).ConfigureAwait(false)
            : null;
        var apiKeyBackup = document.OldApiKeyPresent
            ? await _recoveryMaterialStorage.LoadAsync(ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId)).ConfigureAwait(false)
            : null;

        if (document.OldTokenPresent && tokenBackup is null || document.OldApiKeyPresent && apiKeyBackup is null)
        {
            throw new ConfigurationRecoveryRequiredException(
                GetJournalPath(profileId),
                "The secure-storage recovery snapshot is incomplete.");
        }

        if (document.OldFileExisted && document.OldYaml is null)
        {
            throw new ConfigurationRecoveryRequiredException(
                GetJournalPath(profileId),
                "The previous configuration file content is missing.");
        }

        try
        {
            await using var fileTransaction = document.OldFileExisted
                ? await _fileStore.BeginWriteAsync(
                    document.ServerPath,
                    document.OldYaml!,
                    cancellationToken).ConfigureAwait(false)
                : await _fileStore.BeginDeleteAsync(document.ServerPath, cancellationToken).ConfigureAwait(false);
            await fileTransaction.ApplyAndFlushAsync(cancellationToken).ConfigureAwait(false);

            await RestorePrimarySecretAsync(ConfigurationSecretKeys.GetTokenKey(profileId), document.OldTokenPresent ? tokenBackup : null, cancellationToken).ConfigureAwait(false);
            await RestorePrimarySecretAsync(ConfigurationSecretKeys.GetApiKeyKey(profileId), document.OldApiKeyPresent ? apiKeyBackup : null, cancellationToken).ConfigureAwait(false);
            var rolledBack = document with { State = ConfigurationRecoveryJournalState.RolledBack };
            await WriteJournalAsync(rolledBack, cancellationToken).ConfigureAwait(false);
            fileTransaction.Complete();
            await CleanupRecoveryMaterialAsync(rolledBack, cancellationToken).ConfigureAwait(false);
        }
        catch (ConfigurationRecoveryRequiredException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ConfigurationRecoveryRequiredException(
                GetJournalPath(profileId),
                "The interrupted configuration transaction could not be rolled back.",
                exception);
        }
    }

    private async Task<ConfigurationRecoveryJournal?> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        string? content;
        try
        {
            content = await _fileStore.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationRecoveryRequiredException(
                path,
                "The recovery journal could not be read.",
                exception);
        }
        if (content is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(content, ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryJournal);
        }
        catch (JsonException exception)
        {
            throw new ConfigurationRecoveryRequiredException(path, "The recovery journal is corrupt.", exception);
        }
    }

    private Task WriteJournalAsync(ConfigurationRecoveryJournal document, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(document, ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryJournal);
        return _fileStore.WriteAllTextAsync(GetJournalPath(document.ProfileId), content, cancellationToken);
    }

    private Task WriteBackupAsync(string key, string? value, CancellationToken cancellationToken)
    {
        return value is null
            ? _recoveryMaterialStorage.DeleteAsync(key)
            : _recoveryMaterialStorage.SaveAsync(key, value);
    }

    private async Task ClearBackupSecretsAsync(string profileId, CancellationToken cancellationToken)
    {
        await _recoveryMaterialStorage.DeleteAsync(ConfigurationSecretKeys.GetRecoveryTokenKey(profileId)).ConfigureAwait(false);
        await _recoveryMaterialStorage.DeleteAsync(ConfigurationSecretKeys.GetRecoveryApiKeyKey(profileId)).ConfigureAwait(false);
    }

    private async Task CleanupRecoveryMaterialAsync(
        ConfigurationRecoveryJournal document,
        CancellationToken cancellationToken)
    {
        await CleanupFileArtifactsAsync(document.ServerPath, cancellationToken).ConfigureAwait(false);
        await ClearBackupSecretsAsync(document.ProfileId, cancellationToken).ConfigureAwait(false);
        await _fileStore.DeleteAsync(GetJournalPath(document.ProfileId), cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupFileArtifactsAsync(string serverPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(serverPath)
            ?? throw new ConfigurationRecoveryRequiredException(serverPath, "The configuration recovery path has no directory.");
        var fileName = Path.GetFileName(serverPath);
        foreach (var suffix in new[]
                 {
                     ConfigurationFileTransactionArtifacts.PendingSuffix,
                     ConfigurationFileTransactionArtifacts.RollbackSuffix
                 })
        {
            await foreach (var artifactPath in _fileStore
                               .EnumerateFilesAsync(directory, fileName + suffix + "*", cancellationToken)
                               .ConfigureAwait(false))
            {
                await _fileStore.DeleteAsync(artifactPath, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task RestorePrimarySecretAsync(string key, string? value, CancellationToken cancellationToken)
    {
        // Restoring puts back a value that was already stored before the failed mutation, so it is not new
        // credential material either. Letting a fail-closed policy refuse it would abandon the rollback
        // half-applied and force manual recovery, which is strictly worse than restoring the secret to
        // wherever it already lived.
        return value is null
            ? _recoveryMaterialStorage.DeleteAsync(key)
            : _recoveryMaterialStorage.SaveAsync(key, value);
    }

    private static string GetSafeFileName(string profileId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(profileId))).ToLowerInvariant();
    }

    private void ValidateJournal(ConfigurationRecoveryJournal document, string journalPath)
    {
        if (document.Version != CurrentJournalVersion ||
            string.IsNullOrWhiteSpace(document.TransactionId) ||
            string.IsNullOrWhiteSpace(document.ProfileId) ||
            string.IsNullOrWhiteSpace(document.ServerPath) ||
            !Enum.IsDefined(document.State))
        {
            throw new ConfigurationRecoveryRequiredException(journalPath, "The recovery journal is not understood.");
        }

        var expectedJournalPath = Path.GetFullPath(GetJournalPath(document.ProfileId));
        var expectedServerPath = Path.GetFullPath(
            ConfigurationProfilePaths.GetServerYamlPath(_serversDirectory, document.ProfileId));
        var actualJournalPath = Path.GetFullPath(journalPath);
        var actualServerPath = Path.GetFullPath(document.ServerPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(expectedJournalPath, actualJournalPath, comparison) ||
            !string.Equals(expectedServerPath, actualServerPath, comparison))
        {
            throw new ConfigurationRecoveryRequiredException(
                journalPath,
                "The recovery journal path does not match its configuration profile.");
        }
    }
}

internal sealed record ConfigurationRecoveryJournal(
    int Version,
    string TransactionId,
    string ProfileId,
    string ServerPath,
    string? OldYaml,
    bool OldFileExisted,
    bool OldTokenPresent,
    bool OldApiKeyPresent,
    ConfigurationRecoveryJournalState State);

internal enum ConfigurationRecoveryJournalState
{
    Preparing,
    Ready,
    YamlApplied,
    Committed,
    RolledBack
}

internal sealed class ConfigurationRecoveryRequiredException : IOException
{
    public ConfigurationRecoveryRequiredException(string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
    }

    public string Path { get; }
}

internal sealed class ConfigurationRecoverySecretException : IOException
{
    public ConfigurationRecoverySecretException(Exception innerException)
        : base("Configuration recovery credentials could not be prepared.", innerException)
    {
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConfigurationRecoveryJournal))]
internal partial class ConfigurationRecoveryJsonContext : JsonSerializerContext
{
}

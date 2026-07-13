using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConfigurationSecretSnapshotService
{
    private const string BearerTokenMode = "bearer_token";
    private const string ApiKeyMode = "api_key";
    private const string CloudProviderProfilePrefix = "cloud-provider/";

    private readonly ISecureStorage _secureStorage;
    private readonly IAppFileStore _fileStore;
    private readonly string _serversDirectory;

    public ConfigurationSecretSnapshotService(
        ISecureStorage secureStorage,
        IAppFileStore fileStore,
        IAppDataService appData)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _serversDirectory = Path.Combine(appData.ConfigRootPath, "servers");
    }

    public async Task<ConfigurationSecretSnapshot> ExportAsync(CancellationToken cancellationToken = default)
        => await ExportAsync(null, null, cancellationToken).ConfigureAwait(false);

    public async Task<ConfigurationSecretSnapshot> ExportAsync(
        string? providerId,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new ConfigurationSecretSnapshot();

        await foreach (var path in _fileStore.EnumerateFilesAsync(_serversDirectory, "*.yaml", cancellationToken).ConfigureAwait(false))
        {
            var model = await TryLoadServerYamlAsync(path, cancellationToken).ConfigureAwait(false);
            if (model is null || string.IsNullOrWhiteSpace(model.Id))
            {
                continue;
            }

            var mode = model.Authentication?.Mode?.Trim().ToLowerInvariant() ?? string.Empty;
            if (mode == BearerTokenMode)
            {
                var token = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(model.Id)).ConfigureAwait(false);
                AddSecret(snapshot.Entries, model.Id, BearerTokenMode, token);
                continue;
            }

            if (mode == ApiKeyMode)
            {
                var apiKey = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(model.Id)).ConfigureAwait(false);
                AddSecret(snapshot.Entries, model.Id, ApiKeyMode, apiKey);
            }
        }

        foreach (var registration in CloudConfigSecureStorageKeys.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await ResolveCloudSecretAsync(
                    registration,
                    providerId,
                    secretOverrides)
                .ConfigureAwait(false);
            AddSecret(
                snapshot.Entries,
                CloudProviderProfilePrefix + registration.ProviderId,
                registration.SecretName,
                value);
        }

        return snapshot;
    }

    public async Task ImportAsync(ConfigurationSecretSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        foreach (var entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.ProfileId) || string.IsNullOrWhiteSpace(entry.Kind))
            {
                continue;
            }

            var kind = entry.Kind.Trim().ToLowerInvariant();
            if (entry.ProfileId.StartsWith(CloudProviderProfilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var providerId = entry.ProfileId.Substring(CloudProviderProfilePrefix.Length).Trim();
                if (CloudConfigSecureStorageKeys.TryGetStorageKey(providerId, kind, out var storageKey))
                {
                    await _secureStorage.SaveAsync(storageKey, entry.Value ?? string.Empty).ConfigureAwait(false);
                }

                continue;
            }

            if (kind == BearerTokenMode)
            {
                await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(entry.ProfileId.Trim()), entry.Value ?? string.Empty)
                    .ConfigureAwait(false);
                continue;
            }

            if (kind == ApiKeyMode)
            {
                await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetApiKeyKey(entry.ProfileId.Trim()), entry.Value ?? string.Empty)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<string?> ResolveCloudSecretAsync(
        CloudConfigSecretRegistration registration,
        string? providerId,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides)
    {
        if (!string.Equals(registration.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) ||
            secretOverrides is null ||
            !secretOverrides.TryGetValue(registration.SecretName, out var update) ||
            update.Kind == CloudSecretUpdateKind.KeepExisting)
        {
            return await _secureStorage.LoadAsync(registration.StorageKey).ConfigureAwait(false);
        }

        return update.Kind == CloudSecretUpdateKind.Clear ? null : update.Value;
    }

    private async Task<ServerConfigurationYaml?> TryLoadServerYamlAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return null;
            }

            var model = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml);
            return model.SchemaVersion > 0 ? model : null;
        }
        catch (YamlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void AddSecret(List<ConfigurationSecretEntry> entries, string profileId, string kind, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        entries.Add(new ConfigurationSecretEntry
        {
            ProfileId = profileId.Trim(),
            Kind = kind,
            Value = value
        });
    }
}

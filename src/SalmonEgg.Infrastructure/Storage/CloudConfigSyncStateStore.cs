using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class CloudConfigSyncStateStore
{
    private readonly IAppFileStore _fileStore;
    private readonly string _statePath;

    public CloudConfigSyncStateStore(IAppFileStore fileStore, IAppDataService appData)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _statePath = Path.Combine(appData.AppDataRootPath, "cloud-sync-state.yaml");
    }

    public async Task<CloudConfigSyncState> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(_statePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return new CloudConfigSyncState();
            }

            var state = YamlSerialization.CreateDeserializer().Deserialize<CloudConfigSyncState>(yaml);
            return state.SchemaVersion > 0 ? state : new CloudConfigSyncState();
        }
        catch (YamlException)
        {
            return new CloudConfigSyncState();
        }
        catch (IOException)
        {
            return new CloudConfigSyncState();
        }
    }

    public async Task SaveAsync(CloudConfigSyncState state, CancellationToken cancellationToken = default)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        state.SchemaVersion = 1;
        if (string.IsNullOrWhiteSpace(state.DeviceId))
        {
            state.DeviceId = Guid.NewGuid().ToString("N");
        }

        var yaml = YamlSerialization.CreateSerializer().Serialize(state);
        await _fileStore.WriteAllTextAsync(_statePath, yaml, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileStore.DeleteAsync(_statePath, cancellationToken).ConfigureAwait(false);
    }

    public static DateTimeOffset? ParseLastSync(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

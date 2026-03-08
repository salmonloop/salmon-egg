using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppSettingsService : IAppSettingsService
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _appYamlPath;

    public AppSettingsService()
    {
        _appYamlPath = SalmonEggPaths.GetAppYamlPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_appYamlPath)!);
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_appYamlPath))
        {
            return new AppSettings();
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(_appYamlPath).ConfigureAwait(false);
            var model = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
            if (model.SchemaVersion <= 0)
            {
                return new AppSettings();
            }

            return new AppSettings
            {
                Theme = string.IsNullOrWhiteSpace(model.Theme) ? "System" : model.Theme,
                IsAnimationEnabled = model.IsAnimationEnabled,
                LastSelectedServerId = string.IsNullOrWhiteSpace(model.LastSelectedServerId) ? null : model.LastSelectedServerId
            };
        }
        catch (YamlException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        EnsureWritableSchema(_appYamlPath);

        var model = new AppSettingsYamlV1
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Theme = settings.Theme ?? "System",
            IsAnimationEnabled = settings.IsAnimationEnabled,
            LastSelectedServerId = settings.LastSelectedServerId ?? string.Empty
        };

        var yaml = YamlSerialization.CreateSerializer().Serialize(model);
        await AtomicFile.WriteUtf8AtomicAsync(_appYamlPath, yaml).ConfigureAwait(false);
    }

    private static void EnsureWritableSchema(string appYamlPath)
    {
        if (!File.Exists(appYamlPath))
        {
            return;
        }

        try
        {
            var yaml = File.ReadAllText(appYamlPath);
            var existing = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
            if (existing.SchemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"App settings schema_version {existing.SchemaVersion} is newer than supported version {CurrentSchemaVersion}. Refusing to overwrite.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Existing app.yaml is unreadable; refusing to overwrite.");
        }
    }
}


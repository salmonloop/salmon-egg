using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli.Commands.Config;

/// <summary>
/// Implements the <c>config settings</c> operations over the shared app-settings service.
/// </summary>
/// <remarks>
/// All reads and writes go through <see cref="IAppSettingsService"/>; this handler never touches
/// app.yaml directly. That keeps the atomic-write and Saved-event semantics owned by the service —
/// a CLI write notifies runtime subscribers exactly like a GUI save or a cloud restore would.
/// </remarks>
public sealed class AppSettingsHandler
{
    private readonly ICliOutput _output;
    private readonly IAppSettingsService _appSettings;

    public AppSettingsHandler(ICliOutput output, IAppSettingsService appSettings)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
    }

    /// <summary>
    /// Prints every editable setting with its current value.
    /// </summary>
    public async Task<int> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        foreach (var key in AppSettingValueCatalog.EditableKeys)
        {
            var value = AppSettingValueCatalog.RenderValue(settings, key);
            await _output.WriteAsync($"{key}: {value}").ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    /// <summary>
    /// Updates one setting field and persists it through the shared service.
    /// </summary>
    public async Task<int> SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (!AppSettingValueCatalog.EditableKeys.Contains(key, StringComparer.Ordinal))
        {
            await _output.WriteErrorAsync(
                $"Unknown setting '{key}'. Editable keys: {string.Join(", ", AppSettingValueCatalog.EditableKeys)}.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        if (!AppSettingValueCatalog.TryApply(settings, key, value))
        {
            var allowed = AppSettingValueCatalog.AllowedValues(key);
            var hint = allowed is null
                ? "Expected true/false."
                : $"Allowed values: {string.Join(", ", allowed)}.";
            await _output.WriteErrorAsync($"Invalid value for '{key}': '{value}'. {hint}").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        // SaveAsync owns the schema guard, the atomic write, and the Saved notification.
        try
        {
            await _appSettings.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException exception) when (
            exception.Reason == ConfigurationPersistenceFailureReason.SchemaVersionTooNew)
        {
            // 拒绝写回不是一般写入失败：磁盘上的文件是更新的程序写的，出路是升级而非重试。
            await _output.WriteErrorAsync(exception.UserMessage).ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        var persisted = AppSettingValueCatalog.RenderValue(settings, key);
        await _output.WriteAsync($"{key}: {persisted}").ConfigureAwait(false);
        return CliExitCodes.Success;
    }
}

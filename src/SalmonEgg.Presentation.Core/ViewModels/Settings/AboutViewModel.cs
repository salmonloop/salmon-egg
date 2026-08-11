using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AboutViewModel : ObservableObject
{
    private static readonly Uri DiscordCommunityUri = new("https://discord.gg/wAfJFrYPnf");

    private readonly IPlatformShellService _shell;
    private readonly IAiContentReportLauncher _aiContentReportLauncher;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IStorageLocationService _storageLocations;
    private readonly IAppDataService _paths;
    private readonly IAppDocumentService _documents;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly IReadOnlyList<OpenSourceAcknowledgement> _acknowledgements;

    public string AppName => "SalmonEgg";

    public string AppVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    public string ProtocolVersion => AcpProtocolVersion.Default.ToString();

    public string DocsRootPath => _documents.DocsRootPath;

    public bool CanOpenExternalFiles => _capabilities.SupportsExternalFileOpen;

    public bool CanReportInappropriateAiContent
        => _aiContentReportLauncher.CanReport;

    public IReadOnlyList<OpenSourceAcknowledgementViewModel> OpenSourceAcknowledgements => CreateOpenSourceAcknowledgements();

    public AboutViewModel(
        IPlatformShellService shell,
        IAiContentReportLauncher aiContentReportLauncher,
        IPlatformCapabilityService capabilities,
        IStorageLocationService storageLocations,
        IAppDataService paths,
        IAppDocumentService documents,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        IOpenSourceAcknowledgementsProvider acknowledgementsProvider)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _aiContentReportLauncher = aiContentReportLauncher ?? throw new ArgumentNullException(nameof(aiContentReportLauncher));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(acknowledgementsProvider);
        _acknowledgements = acknowledgementsProvider.GetAcknowledgements()
            ?? Array.Empty<OpenSourceAcknowledgement>();
    }

    private IReadOnlyList<OpenSourceAcknowledgementViewModel> CreateOpenSourceAcknowledgements()
        => _acknowledgements
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new OpenSourceAcknowledgementViewModel(
                item.Name.Trim(),
                string.IsNullOrWhiteSpace(item.Version)
                    ? _localizer["About_AcknowledgementVersionFallback"]
                    : item.Version.Trim(),
                string.IsNullOrWhiteSpace(item.License)
                    ? _localizer["About_AcknowledgementLicenseFallback"]
                    : item.License.Trim(),
                string.IsNullOrWhiteSpace(item.SourceUrl)
                    ? _localizer["About_AcknowledgementSourceFallback"]
                    : item.SourceUrl.Trim()))
            .ToArray();

    [RelayCommand]
    private async Task JoinDiscordCommunityAsync()
    {
        if (!await _shell.OpenUriAsync(DiscordCommunityUri).ConfigureAwait(true))
        {
            await _ui.ShowInfoAsync(_localizer["About_DiscordOpenFailed"]).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync()
    {
        return OpenStorageLocationAsync(AppStorageLocation.AppData);
    }

    [RelayCommand]
    private async Task OpenReleaseNotesAsync()
    {
        var path = _documents.GetReleaseNotesPath();
        if (!await _documents.ExistsAsync(path))
        {
            await NotifyMissingDocAsync(path, _localizer["About_ReleaseNotesTitle"]);
            return;
        }

        await OpenFileOrNotifyAsync(path);
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        var path = _documents.GetPrivacyPolicyPath();
        if (!await _documents.ExistsAsync(path))
        {
            await NotifyMissingDocAsync(path, _localizer["About_PrivacyPolicyTitle"]);
            return;
        }

        await OpenFileOrNotifyAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanReportInappropriateAiContent))]
    private async Task ReportInappropriateAiContentAsync()
    {
        if (!await _aiContentReportLauncher.TryOpenReportAsync(
                AppName,
                AppVersion,
                ProtocolVersion).ConfigureAwait(true))
        {
            await NotifyReportOpenFailedAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task CopyVersionInfoAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{_localizer["About_VersionInfoAppLabel"]}: {AppName}");
        sb.AppendLine($"{_localizer["About_VersionInfoVersionLabel"]}: {AppVersion}");
        sb.AppendLine($"{_localizer["About_VersionInfoProtocolLabel"]}: {ProtocolVersion}");
        var copied = await _shell.CopyToClipboardAsync(sb.ToString());
        await _ui.ShowInfoAsync(copied
                ? _localizer["About_VersionInfoCopied"]
                : _localizer["About_ClipboardUnsupported"]);
    }

    private async Task NotifyMissingDocAsync(string path, string title)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        var message = folder == null
            ? _localizer["About_MissingDocumentMessage", title]
            : _localizer["About_MissingDocumentWithFolderMessage", title, folder];

        await _ui.ShowInfoAsync(message);

        if (CanOpenExternalFiles && !string.IsNullOrWhiteSpace(folder))
        {
            _ = await _storageLocations.OpenExistingFolderAsync(folder);
        }
    }

    private async Task OpenStorageLocationAsync(AppStorageLocation location)
    {
        if (!await _storageLocations.OpenAsync(location))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private async Task OpenFileOrNotifyAsync(string path)
    {
        if (!await _shell.OpenFileAsync(path))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private Task NotifyExternalOpenUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_ExternalOpenUnsupported"]);

    private Task NotifyReportOpenFailedAsync()
        => _ui.ShowInfoAsync(_localizer["AiContentReport_OpenFailed"]);
}

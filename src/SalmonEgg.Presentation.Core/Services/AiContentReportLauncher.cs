using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class AiContentReportLauncher : IAiContentReportLauncher
{
    private readonly IAppSupportInfoService _supportInfo;
    private readonly IPlatformShellService _shell;
    private readonly IStringLocalizer<CoreStrings> _localizer;

    public AiContentReportLauncher(
        IAppSupportInfoService supportInfo,
        IPlatformShellService shell,
        IStringLocalizer<CoreStrings> localizer)
    {
        _supportInfo = supportInfo ?? throw new ArgumentNullException(nameof(supportInfo));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public bool CanReport
        => !string.IsNullOrWhiteSpace(_supportInfo.ReportInappropriateAiContentEmail);

    public async Task<bool> TryOpenReportAsync(
        string appName,
        string appVersion,
        string protocolVersion,
        string? contentExcerpt = null)
    {
        var uri = AiContentReportUriBuilder.TryCreate(
            email: _supportInfo.ReportInappropriateAiContentEmail,
            subject: _localizer["About_ReportAiContentSubject"],
            appLabel: _localizer["About_VersionInfoAppLabel"],
            appName: appName ?? string.Empty,
            versionLabel: _localizer["About_VersionInfoVersionLabel"],
            appVersion: appVersion ?? string.Empty,
            protocolLabel: _localizer["About_VersionInfoProtocolLabel"],
            protocolVersion: protocolVersion ?? string.Empty,
            bodyPrompt: _localizer["About_ReportAiContentBodyPrompt"],
            contentExcerptLabel: string.IsNullOrWhiteSpace(contentExcerpt)
                ? null
                : _localizer["Chat_ReportAiContentExcerptLabel"].Value,
            contentExcerpt: contentExcerpt);

        if (uri is null)
        {
            return false;
        }

        return await _shell.OpenUriAsync(uri).ConfigureAwait(true);
    }
}

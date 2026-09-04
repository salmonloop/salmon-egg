using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Models.Cli;

namespace SalmonEgg.Presentation.ViewModels.Settings;

/// <summary>
/// Shows whether this app's <c>salmon-egg</c> command is reachable from a shell, and manages it where the
/// app is allowed to.
/// </summary>
/// <remarks>
/// Everything here is a projection of one observation. There is no state to persist and nothing to cache:
/// the answer lives in the machine's PATH, which an installer, another installation or the user can change
/// while this page is open, so it is re-read on every refresh rather than remembered.
///
/// Which actions exist is a platform capability, not a preference. On Windows and Linux the installer owns
/// the registration and this page is read-only — offering a "fix" would create a second owner for one path,
/// and the uninstall that runs second would leave a command pointing at a deleted app. macOS is the
/// exception because a dragged .app has no install hook at all.
/// </remarks>
public sealed partial class CommandLineSettingsViewModel : ObservableObject
{
    private readonly ICliCommandRegistrationInspector _inspector;
    private readonly ICliCommandLinkService _linkService;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<CommandLineSettingsViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(LinkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlinkCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _actionMessage;

    [ObservableProperty]
    private CliCommandStatusSeverity _actionSeverity = CliCommandStatusSeverity.Informational;

    private CliCommandRegistration? _registration;

    public CommandLineSettingsViewModel(
        ICliCommandRegistrationInspector inspector,
        ICliCommandLinkService linkService,
        IPlatformCapabilityService capabilities,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<CommandLineSettingsViewModel> logger)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _linkService = linkService ?? throw new ArgumentNullException(nameof(linkService));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The command name, so the page can show what to type without repeating the literal.</summary>
    public string CommandName => CliCommandNames.Command;

    public bool IsInspectionSupported => _capabilities.SupportsCliCommandInspection;

    /// <summary>True on the one platform where the app, not an installer, owns the PATH entry.</summary>
    public bool CanManageLink => _linkService.IsSupported;

    /// <summary>
    /// True where the registration belongs to the installer, which is what the page says instead of
    /// offering an action it must not take.
    /// </summary>
    public bool IsInstallerOwned => IsInspectionSupported && !CanManageLink;

    public CliCommandStatusSeverity Severity => _registration?.State switch
    {
        CliCommandRegistrationState.Registered => CliCommandStatusSeverity.Success,
        CliCommandRegistrationState.VersionMismatch => CliCommandStatusSeverity.Warning,
        CliCommandRegistrationState.Unreadable => CliCommandStatusSeverity.Error,
        CliCommandRegistrationState.NotRegistered => CliCommandStatusSeverity.Warning,
        _ => CliCommandStatusSeverity.Informational,
    };

    public string StatusTitle => Localize(_registration?.State switch
    {
        CliCommandRegistrationState.Registered => "CommandLine_Status_Registered",
        CliCommandRegistrationState.VersionMismatch => "CommandLine_Status_VersionMismatch",
        CliCommandRegistrationState.Unreadable => "CommandLine_Status_Unreadable",
        CliCommandRegistrationState.NotRegistered => "CommandLine_Status_NotRegistered",
        CliCommandRegistrationState.Unsupported => "CommandLine_Status_Unsupported",
        _ => "CommandLine_Status_Unknown",
    });

    public string StatusMessage => _registration?.State switch
    {
        CliCommandRegistrationState.Registered => Localize("CommandLine_Message_Registered"),
        CliCommandRegistrationState.VersionMismatch => Localize("CommandLine_Message_VersionMismatch"),
        // The detail is the operator-facing part and is not localized: it is whatever the executable or the
        // operating system said, and translating it would lose the text a user has to search for.
        CliCommandRegistrationState.Unreadable => $"{Localize("CommandLine_Message_Unreadable")} {_registration.FailureDetail}",
        CliCommandRegistrationState.NotRegistered => Localize(
            CanManageLink ? "CommandLine_Message_NotRegistered_CanLink" : "CommandLine_Message_NotRegistered_Installer"),
        CliCommandRegistrationState.Unsupported => Localize("CommandLine_Message_Unsupported"),
        _ => Localize("CommandLine_Message_Unknown"),
    };

    public string? ResolvedPath => _registration?.ResolvedPath;

    public bool HasResolvedPath => !string.IsNullOrEmpty(ResolvedPath);

    public string? ResolvedTargetPath => _registration?.ResolvedTargetPath;

    public bool HasResolvedTargetPath => !string.IsNullOrEmpty(ResolvedTargetPath);

    public string? ReportedVersion => _registration?.ReportedVersion;

    public bool HasReportedVersion => !string.IsNullOrEmpty(ReportedVersion);

    public string ExpectedVersion => _registration?.ExpectedVersion ?? string.Empty;

    /// <summary>True once an inspection has produced a result, so the page can hold detail rows back.</summary>
    public bool HasResult => _registration is not null;

    public bool HasActionMessage => !string.IsNullOrEmpty(ActionMessage);

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            // The whole projection changes together, so it is replaced in one assignment rather than
            // property by property: a partially updated status is a status describing two different machines.
            _registration = await _inspector.InspectAsync(cancellationToken).ConfigureAwait(true);
            NotifyStatusChanged();
        }
        catch (OperationCanceledException)
        {
            // The page was navigated away from mid-inspection. Nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inspecting the salmon-egg command registration failed.");
            _registration = null;
            NotifyStatusChanged();
            ReportAction(CliCommandStatusSeverity.Error, Localize("CommandLine_Action_InspectFailed"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageLinkNow))]
    private Task LinkAsync(CancellationToken cancellationToken) =>
        RunLinkOperationAsync(_linkService.LinkAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanManageLinkNow))]
    private Task UnlinkAsync(CancellationToken cancellationToken) =>
        RunLinkOperationAsync(_linkService.UnlinkAsync, cancellationToken);

    private bool CanRunOperation() => !IsBusy;

    private bool CanManageLinkNow() => CanManageLink && !IsBusy;

    private async Task RunLinkOperationAsync(
        Func<CancellationToken, Task<CliCommandLinkResult>> operation,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(true);
            ReportAction(result);

            // Re-inspect regardless of the outcome: a cancelled authorization leaves the previous state, and
            // a failure may still have changed something. Reporting the operation's result as the new status
            // would be reporting an intention rather than an observation.
            if (result.Outcome is not CliCommandLinkOutcome.Cancelled)
            {
                _registration = await _inspector.InspectAsync(cancellationToken).ConfigureAwait(true);
                NotifyStatusChanged();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Managing the salmon-egg command link failed.");
            ReportAction(CliCommandStatusSeverity.Error, Localize("CommandLine_Action_LinkFailed"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportAction(CliCommandLinkResult result)
    {
        switch (result.Outcome)
        {
            case CliCommandLinkOutcome.Linked:
                ReportAction(CliCommandStatusSeverity.Success, Localize("CommandLine_Action_Linked"));
                break;
            case CliCommandLinkOutcome.Unlinked:
                ReportAction(CliCommandStatusSeverity.Success, Localize("CommandLine_Action_Unlinked"));
                break;
            case CliCommandLinkOutcome.Cancelled:
                // A dismissed authorization dialog is a decision, not an error.
                ReportAction(CliCommandStatusSeverity.Informational, Localize("CommandLine_Action_Cancelled"));
                break;
            case CliCommandLinkOutcome.Unsupported:
                ReportAction(CliCommandStatusSeverity.Warning, Localize("CommandLine_Action_Unsupported"));
                break;
            default:
                ReportAction(
                    CliCommandStatusSeverity.Error,
                    $"{Localize("CommandLine_Action_LinkFailed")} {result.Detail}".TrimEnd());
                break;
        }
    }

    private void ReportAction(CliCommandStatusSeverity severity, string message)
    {
        ActionSeverity = severity;
        ActionMessage = message;
        OnPropertyChanged(nameof(HasActionMessage));
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ResolvedPath));
        OnPropertyChanged(nameof(HasResolvedPath));
        OnPropertyChanged(nameof(ResolvedTargetPath));
        OnPropertyChanged(nameof(HasResolvedTargetPath));
        OnPropertyChanged(nameof(ReportedVersion));
        OnPropertyChanged(nameof(HasReportedVersion));
        OnPropertyChanged(nameof(ExpectedVersion));
        OnPropertyChanged(nameof(HasResult));
    }

    private string Localize(string key)
    {
        var localized = _localizer[key];
        return string.IsNullOrWhiteSpace(localized.Value) ? key : localized.Value;
    }
}

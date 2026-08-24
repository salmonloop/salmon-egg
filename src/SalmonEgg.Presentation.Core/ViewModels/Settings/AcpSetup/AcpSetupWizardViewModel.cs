using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

/// <summary>
/// Step machine for the ACP setup wizard: detect agents, install what is missing, fill in launch
/// parameters, prove the configuration works, then save it as a connection profile.
/// </summary>
/// <remarks>
/// Owns no UI types and performs no I/O of its own — every side effect goes through
/// <see cref="AcpSetupWizardOrchestrator"/>, so this type stays unit-testable on every platform.
///
/// Advancing is blocked only on facts the wizard actually knows. A component probed as
/// <see cref="AcpComponentAvailability.Undetermined"/> does not block, because an unanswerable probe is
/// not evidence of absence; the end-to-end test is the real gate before saving.
/// </remarks>
public sealed partial class AcpSetupWizardViewModel : ObservableObject
{
    private const int MaxInstallOutputLines = 200;

    private readonly AcpSetupWizardOrchestrator _orchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private readonly ILogger<AcpSetupWizardViewModel> _logger;

    public AcpSetupWizardViewModel(
        AcpSetupWizardOrchestrator orchestrator,
        IUiDispatcher uiDispatcher,
        ILogger<AcpSetupWizardViewModel> logger,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localizer = localizer;

        foreach (var agent in _orchestrator.Agents)
        {
            var row = new AcpSetupAgentRowViewModel(agent, _localizer);
            row.InstallRequested += InstallAgentRow;
            Agents.Add(row);
        }
    }

    /// <summary>Catalog agents, each carrying its own runtime probe.</summary>
    public ObservableCollection<AcpSetupAgentRowViewModel> Agents { get; } = new();

    /// <summary>Adapters offered for the selected agent, in catalog display order.</summary>
    public ObservableCollection<AcpAdapterDescriptor> Adapters { get; } = new();

    /// <summary>Editable launch parameters for the selected adapter.</summary>
    public ObservableCollection<AcpSetupParameterRowViewModel> Parameters { get; } = new();

    /// <summary>
    /// Live installer output, capped so a chatty installer cannot grow without bound.
    /// </summary>
    /// <remarks>
    /// <see cref="LatestInstallOutputLine"/> and <see cref="HasInstallOutput"/> are computed from this
    /// collection, and a collection's own change notifications say nothing about properties derived from
    /// it. Every mutation therefore goes through <see cref="AppendInstallOutput"/> or
    /// <see cref="ResetInstallOutput"/>, which raise those two — mutating this collection directly
    /// leaves the surface bound to a stale value.
    /// </remarks>
    public ObservableCollection<string> InstallOutput { get; } = new();

    /// <summary>True when this platform can install components rather than only linking documentation.</summary>
    public bool SupportsAutomaticInstall => _orchestrator.SupportsAutomaticInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    [NotifyPropertyChangedFor(nameof(IsOnAgentSelection))]
    [NotifyPropertyChangedFor(nameof(IsOnComponentSetup))]
    [NotifyPropertyChangedFor(nameof(IsOnParameters))]
    [NotifyPropertyChangedFor(nameof(IsOnTest))]
    [NotifyPropertyChangedFor(nameof(IsOnSave))]
    [NotifyPropertyChangedFor(nameof(StepPositionText))]
    private AcpSetupWizardStep _step = AcpSetupWizardStep.AgentSelection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallRuntimeCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAgentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallRuntimeCommand))]
    private AcpSetupAgentRowViewModel? _selectedAgent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAdapterCommand))]
    private AcpAdapterDescriptor? _selectedAdapter;

    /// <summary>Latest adapter probe for the selected adapter, or null before it has been probed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyPropertyChangedFor(nameof(IsAdapterMissing))]
    [NotifyPropertyChangedFor(nameof(IsAdapterUsable))]
    [NotifyPropertyChangedFor(nameof(IsAdapterUndetermined))]
    private AcpComponentProbeResult? _adapterProbe;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _profileName = string.Empty;

    /// <summary>Latest end-to-end test result, or null before the first test.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    [NotifyPropertyChangedFor(nameof(IsTestSuccessful))]
    [NotifyPropertyChangedFor(nameof(TestFailureStageText))]
    [NotifyPropertyChangedFor(nameof(TestRemediationText))]
    private AcpSetupTestResult? _testResult;

    /// <summary>Single-line rendering of the command the wizard will save, for user review.</summary>
    [ObservableProperty]
    private string _launchCommandPreview = string.Empty;

    /// <summary>Operation failure message, empty when the last operation succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _errorMessage = string.Empty;

    /// <summary>The profile the wizard saved, or null until the save step completes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedProfile))]
    private ServerConfiguration? _savedProfile;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Most recent installer output line, or empty before any install has run.</summary>
    public string LatestInstallOutputLine => InstallOutput.Count > 0 ? InstallOutput[^1] : string.Empty;

    /// <summary>True once an installer has produced at least one output line.</summary>
    public bool HasInstallOutput => InstallOutput.Count > 0;

    public bool IsOnAgentSelection => Step == AcpSetupWizardStep.AgentSelection;

    public bool IsOnComponentSetup => Step == AcpSetupWizardStep.ComponentSetup;

    public bool IsOnParameters => Step == AcpSetupWizardStep.Parameters;

    public bool IsOnTest => Step == AcpSetupWizardStep.Test;

    public bool IsOnSave => Step == AcpSetupWizardStep.Save;

    /// <summary>Human-readable step position ("Step 2 of 5"), so the walk's place is always stated.</summary>
    public string StepPositionText => Localize(
        "AcpSetup_Step_Position",
        $"Step {(int)Step + 1} of {TotalSteps}")
        .Replace("{0}", ((int)Step + 1).ToString(System.Globalization.CultureInfo.CurrentCulture))
        .Replace("{1}", TotalSteps.ToString(System.Globalization.CultureInfo.CurrentCulture));

    private const int TotalSteps = 5;

    public bool HasSavedProfile => SavedProfile is not null;

    public bool IsAdapterUsable => AdapterProbe?.IsUsable == true;

    public bool IsAdapterMissing => AdapterProbe?.Availability == AcpComponentAvailability.Missing;

    public bool IsAdapterUndetermined
        => AdapterProbe?.Availability == AcpComponentAvailability.Undetermined;

    public bool HasTestResult => TestResult is not null;

    public bool IsTestSuccessful => TestResult?.IsSuccess == true;

    /// <summary>Localized name of the stage a failed test reached, empty on success.</summary>
    public string TestFailureStageText
        => TestResult is null || TestResult.IsSuccess
            ? string.Empty
            : Localize(StageKeys.Resolve(TestResult.Stage), TestResult.Stage.ToString());

    /// <summary>Localized remediation advice for a failed test, empty when none applies.</summary>
    public string TestRemediationText
        => string.IsNullOrEmpty(TestResult?.RemediationKey)
            ? string.Empty
            : Localize(TestResult!.RemediationKey!, string.Empty);

    // ── Detection ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task DetectAgentsAsync(CancellationToken cancellationToken)
    {
        await RunOperationAsync(
            async token =>
            {
                MarkAgentsChecking();
                var states = await _orchestrator.DetectAgentsAsync(token).ConfigureAwait(false);
                await _uiDispatcher.EnqueueAsync(() => ApplyRuntimeProbes(states)).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanDetectAdapter))]
    private async Task DetectAdapterAsync(CancellationToken cancellationToken)
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
        {
            return;
        }

        await RunOperationAsync(
            async token =>
            {
                var probe = await _orchestrator
                    .DetectComponentAsync(adapter.Component, token)
                    .ConfigureAwait(false);
                await _uiDispatcher.EnqueueAsync(() => AdapterProbe = probe).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanDetectAdapter() => !IsBusy && SelectedAdapter is not null;

    // ── Installation ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanInstallRuntime))]
    private async Task InstallRuntimeAsync(CancellationToken cancellationToken)
    {
        var row = SelectedAgent;
        if (row is null)
        {
            return;
        }

        await RunOperationAsync(
            async token =>
            {
                var (install, probe) = await _orchestrator
                    .InstallComponentAsync(row.Agent.Runtime, AppendInstallOutput, token)
                    .ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        row.Runtime = probe;
                        ReportInstallFailure(install);
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanInstallRuntime()
        => !IsBusy
            && SelectedAgent is not null
            && SupportsAutomaticInstall
            && SelectedAgent.SupportsAutomaticInstall;

    /// <summary>Per-row install entry point; keeps busy/error handling in one place.</summary>
    private void InstallAgentRow(AcpSetupAgentRowViewModel row)
    {
        if (IsBusy)
        {
            return;
        }

        _ = RunOperationAsync(
            async token =>
            {
                var (install, probe) = await _orchestrator
                    .InstallComponentAsync(row.Agent.Runtime, AppendInstallOutput, token)
                    .ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        row.Runtime = probe;
                        ReportInstallFailure(install);
                    })
                    .ConfigureAwait(false);
            },
            CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanInstallAdapter))]
    private async Task InstallAdapterAsync(CancellationToken cancellationToken)
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
        {
            return;
        }

        await RunOperationAsync(
            async token =>
            {
                var (install, probe) = await _orchestrator
                    .InstallComponentAsync(adapter.Component, AppendInstallOutput, token)
                    .ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        AdapterProbe = probe;
                        ReportInstallFailure(install);
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanInstallAdapter()
        => !IsBusy
            && SelectedAdapter is not null
            && SupportsAutomaticInstall
            && SelectedAdapter.Component.SupportsAutomaticInstall;

    // ── Test and save ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        var draft = BuildDraft();
        if (draft is null)
        {
            return;
        }

        await RunOperationAsync(
            async token =>
            {
                var result = await _orchestrator.TestDraftAsync(draft, token).ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        TestResult = result;
                        ApplyValidationMessages(result);
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanTest() => !IsBusy && SelectedAdapter is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // RelayCommand invocation does not consult CanExecute, so the rule is restated here: a
        // profile that never passed the test must not reach the connection list looking verified.
        if (!CanSave())
        {
            return;
        }

        var draft = BuildDraft();
        if (draft is null)
        {
            return;
        }

        await RunOperationAsync(
            async token =>
            {
                var saved = await _orchestrator.SaveDraftAsync(draft, token).ConfigureAwait(false);
                await _uiDispatcher.EnqueueAsync(() => SavedProfile = saved).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saving requires a successful test. The issue's acceptance criteria put the test before the save,
    /// and a profile saved without one would appear in the connection list as if it had been verified.
    /// </summary>
    private bool CanSave()
        => !IsBusy
            && IsTestSuccessful
            && SelectedAdapter is not null
            && !string.IsNullOrWhiteSpace(ProfileName);

    // ── Step machine ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoNextAsync(CancellationToken cancellationToken)
    {
        switch (Step)
        {
            case AcpSetupWizardStep.AgentSelection:
                PrepareComponentSetup();
                Step = AcpSetupWizardStep.ComponentSetup;
                await DetectAdapterAsync(cancellationToken).ConfigureAwait(false);
                break;
            case AcpSetupWizardStep.ComponentSetup:
                PrepareParameters();
                Step = AcpSetupWizardStep.Parameters;
                break;
            case AcpSetupWizardStep.Parameters:
                if (!TryPrepareTest())
                {
                    return;
                }

                Step = AcpSetupWizardStep.Test;
                break;
            case AcpSetupWizardStep.Test:
                PrepareSave();
                Step = AcpSetupWizardStep.Save;
                break;
            case AcpSetupWizardStep.Save:
            default:
                break;
        }
    }

    private bool CanGoNext() => !IsBusy && Step switch
    {
        AcpSetupWizardStep.AgentSelection => SelectedAgent is not null,
        // Only a probe that positively found nothing blocks the walk; "undetermined" does not.
        AcpSetupWizardStep.ComponentSetup =>
            SelectedAdapter is not null
                && SelectedAgent?.IsMissing != true
                && !IsAdapterMissing,
        AcpSetupWizardStep.Parameters => true,
        AcpSetupWizardStep.Test => IsTestSuccessful,
        _ => false
    };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (Step == AcpSetupWizardStep.AgentSelection)
        {
            return;
        }

        Step = (AcpSetupWizardStep)((int)Step - 1);
        ErrorMessage = string.Empty;
    }

    private bool CanGoBack() => Step != AcpSetupWizardStep.AgentSelection;

    // ── Step preparation ────────────────────────────────────────────────────

    private void PrepareComponentSetup()
    {
        var agent = SelectedAgent?.Agent;
        Adapters.Clear();
        ResetInstallOutput();
        AdapterProbe = null;
        if (agent is null)
        {
            SelectedAdapter = null;
            return;
        }

        foreach (var adapter in agent.Adapters)
        {
            Adapters.Add(adapter);
        }

        SelectedAdapter = agent.ResolveRecommendedAdapter();
    }

    private void PrepareParameters()
    {
        Parameters.Clear();
        TestResult = null;
        var template = SelectedAdapter?.LaunchTemplate;
        if (template is null)
        {
            LaunchCommandPreview = string.Empty;
            return;
        }

        foreach (var definition in template.Parameters)
        {
            Parameters.Add(
                new AcpSetupParameterRowViewModel(definition, RefreshLaunchCommandPreview, _localizer));
        }

        RefreshLaunchCommandPreview();
    }

    /// <summary>
    /// Validates before the test step so obviously incomplete input is corrected in the form the user is
    /// already looking at, instead of coming back as a test failure.
    /// </summary>
    private bool TryPrepareTest()
    {
        var template = SelectedAdapter?.LaunchTemplate;
        if (template is null)
        {
            return false;
        }

        var violations = AcpSetupParameterValidator.Validate(template, CollectParameterValues());
        ApplyViolations(violations);
        if (violations.Count > 0)
        {
            return false;
        }

        TestResult = null;
        RefreshLaunchCommandPreview();
        return true;
    }

    private void PrepareSave()
    {
        if (string.IsNullOrWhiteSpace(ProfileName) && SelectedAgent is not null)
        {
            ProfileName = SelectedAgent.DisplayName;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private AcpSetupDraft? BuildDraft()
    {
        var agent = SelectedAgent?.Agent;
        var adapter = SelectedAdapter;
        if (agent is null || adapter is null)
        {
            return null;
        }

        return new AcpSetupDraft
        {
            Agent = agent,
            Adapter = adapter,
            ParameterValues = CollectParameterValues(),
            ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? agent.DisplayName : ProfileName.Trim()
        };
    }

    private Dictionary<string, string> CollectParameterValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Parameters)
        {
            values[row.Key] = row.Value;
        }

        return values;
    }

    private void RefreshLaunchCommandPreview()
    {
        var template = SelectedAdapter?.LaunchTemplate;
        if (template is null)
        {
            LaunchCommandPreview = string.Empty;
            return;
        }

        try
        {
            LaunchCommandPreview = AcpLaunchPlanBuilder
                .Build(template, CollectParameterValues())
                .CommandLineDisplay;
        }
        catch (InvalidOperationException ex)
        {
            // The builder refuses to place a secret into a persisted plan. Surface it rather than
            // rendering a partial command that hides why the preview stopped updating.
            LaunchCommandPreview = string.Empty;
            _logger.LogWarning(ex, "ACP setup launch preview rejected the current parameter values.");
        }
    }

    private void MarkAgentsChecking()
    {
        _uiDispatcher.Enqueue(() =>
        {
            foreach (var row in Agents)
            {
                row.Runtime = new AcpComponentProbeResult
                {
                    ComponentId = row.Agent.Runtime.Id,
                    Availability = AcpComponentAvailability.Checking
                };
            }
        });
    }

    private void ApplyRuntimeProbes(IReadOnlyList<AcpAgentDetectionState> states)
    {
        var byAgentId = new Dictionary<string, AcpComponentProbeResult>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            byAgentId[state.Agent.Id] = state.Runtime;
        }

        foreach (var row in Agents)
        {
            if (byAgentId.TryGetValue(row.AgentId, out var probe))
            {
                row.Runtime = probe;
            }
        }
    }

    private void ApplyViolations(IReadOnlyList<AcpSetupParameterViolation> violations)
    {
        foreach (var row in Parameters)
        {
            row.ValidationMessage = string.Empty;
        }

        foreach (var violation in violations)
        {
            foreach (var row in Parameters)
            {
                if (string.Equals(row.Key, violation.ParameterKey, StringComparison.Ordinal))
                {
                    row.ValidationMessage = Localize(violation.MessageKey, violation.MessageKey);
                }
            }
        }
    }

    /// <summary>
    /// Mirrors a validation-stage test failure back onto the row it belongs to, so the user sees the
    /// problem in the form rather than only in the test panel.
    /// </summary>
    private void ApplyValidationMessages(AcpSetupTestResult result)
    {
        if (result.IsSuccess || result.Stage != AcpSetupTestStage.Validation)
        {
            return;
        }

        var template = SelectedAdapter?.LaunchTemplate;
        if (template is null)
        {
            return;
        }

        ApplyViolations(AcpSetupParameterValidator.Validate(template, CollectParameterValues()));
    }

    private void AppendInstallOutput(string line)
    {
        _uiDispatcher.Enqueue(() =>
        {
            InstallOutput.Add(line);
            while (InstallOutput.Count > MaxInstallOutputLines)
            {
                InstallOutput.RemoveAt(0);
            }

            NotifyInstallOutputChanged();
        });
    }

    /// <summary>Clears installer output so a new component's install does not inherit the last one's.</summary>
    private void ResetInstallOutput()
    {
        InstallOutput.Clear();
        NotifyInstallOutputChanged();
    }

    private void NotifyInstallOutputChanged()
    {
        OnPropertyChanged(nameof(LatestInstallOutputLine));
        OnPropertyChanged(nameof(HasInstallOutput));
    }

    private void ReportInstallFailure(AcpComponentInstallResult install)
    {
        if (install.IsSuccess)
        {
            return;
        }

        ErrorMessage = install.ErrorDetail ?? Localize(InstallFailedKey, "Installation failed.");
    }

    private bool CanRunOperation() => !IsBusy;

    /// <summary>
    /// Runs one wizard operation with the shared busy flag and failure surface, so no command has to
    /// re-implement that bookkeeping and none can leave the wizard stuck busy.
    /// </summary>
    private async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a user action, not a failure to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACP setup wizard operation failed.");
            await _uiDispatcher
                .EnqueueAsync(() => ErrorMessage = ex.Message)
                .ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.EnqueueAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private string Localize(string key, string fallback)
        => CoreStringResolver.Resolve(_localizer, key, fallback);

    private const string InstallFailedKey = "AcpSetup_Install_Failed";

    /// <summary>Localization keys for the stage a failed test reached.</summary>
    private static class StageKeys
    {
        public static string Resolve(AcpSetupTestStage stage) => stage switch
        {
            AcpSetupTestStage.Validation => "AcpSetup_Stage_Validation",
            AcpSetupTestStage.CommandResolution => "AcpSetup_Stage_CommandResolution",
            AcpSetupTestStage.AdapterStartup => "AcpSetup_Stage_AdapterStartup",
            AcpSetupTestStage.Handshake => "AcpSetup_Stage_Handshake",
            _ => "AcpSetup_Stage_Completed"
        };
    }
}

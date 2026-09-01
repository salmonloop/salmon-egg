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
    private readonly TimeProvider _timeProvider;
    private bool _suppressAdapterSelectionProbe;

    public AcpSetupWizardViewModel(
        AcpSetupWizardOrchestrator orchestrator,
        IUiDispatcher uiDispatcher,
        ILogger<AcpSetupWizardViewModel> logger,
        IStringLocalizer<CoreStrings>? localizer = null,
        TimeProvider? timeProvider = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localizer = localizer;
        _timeProvider = timeProvider ?? TimeProvider.System;

        foreach (var agent in _orchestrator.Agents)
        {
            var row = new AcpSetupAgentRowViewModel(agent, _localizer);
            row.InstallRequested += InstallAgentRow;
            row.InstallToolchainRequested += InstallAgentToolchain;
            row.VerifyRequested += VerifyAgentRow;
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
    /// Live output of the install currently running or last run, capped so a chatty installer cannot grow
    /// without bound.
    /// </summary>
    /// <remarks>
    /// One collection for every install entry point, because the surface showing it is shared: a runtime
    /// install starts from an agent row on the selection step and an adapter install from the component
    /// step, and both are watched in the same place. Each install therefore clears this before it begins,
    /// so the log on screen always belongs to the component named beside it.
    ///
    /// <see cref="LatestInstallOutputLine"/> and <see cref="HasInstallOutput"/> are computed from this
    /// collection, and a collection's own change notifications say nothing about properties derived from
    /// it. Every mutation therefore goes through <see cref="AppendInstallOutput"/> or
    /// <see cref="ResetInstallOutput"/>, which raise those two and marshal to the UI thread — mutating this
    /// collection directly leaves the surface bound to a stale value, and does so from whichever thread the
    /// caller happened to be on.
    /// </remarks>
    public ObservableCollection<string> InstallOutput { get; } = new();

    /// <summary>True when this platform can install components rather than only linking documentation.</summary>
    public bool SupportsAutomaticInstall => _orchestrator.SupportsAutomaticInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipTestCommand))]
    [NotifyPropertyChangedFor(nameof(IsOnAgentSelection))]
    [NotifyPropertyChangedFor(nameof(IsOnComponentSetup))]
    [NotifyPropertyChangedFor(nameof(IsOnParameters))]
    [NotifyPropertyChangedFor(nameof(IsOnTest))]
    [NotifyPropertyChangedFor(nameof(IsOnSave))]
    [NotifyPropertyChangedFor(nameof(IsSkipTestVisible))]
    [NotifyPropertyChangedFor(nameof(StepPositionText))]
    private AcpSetupWizardStep _step = AcpSetupWizardStep.AgentSelection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallToolchainCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAgentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipTestCommand))]
    // Cancel is the one command enabled *by* being busy, so it tracks the same flag in the opposite
    // direction rather than needing a signal of its own.
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyPropertyChangedFor(nameof(StepPositionText))]
    private AcpSetupAgentRowViewModel? _selectedAgent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallToolchainCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAdapterCommand))]
    [NotifyPropertyChangedFor(nameof(AdapterProbeCommand))]
    [NotifyPropertyChangedFor(nameof(HasAdapterProbeCommand))]
    // Read by IsAdapterToolchainMissing for the component's install path.
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainMissing))]
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainInstallable))]
    [NotifyPropertyChangedFor(nameof(MissingAdapterToolchainName))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainMissingText))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainDocumentation))]
    [NotifyPropertyChangedFor(nameof(StepPositionText))]
    private AcpAdapterDescriptor? _selectedAdapter;

    /// <summary>
    /// A verdict belongs to one adapter. Changing the picker therefore clears the previous verdict and
    /// probes the newly selected adapter; otherwise a successful adapter A probe could incorrectly let
    /// adapter B proceed to the launch step.
    /// </summary>
    partial void OnSelectedAdapterChanged(AcpAdapterDescriptor? value)
    {
        AdapterProbe = null;
        AdapterToolchain = null;
        TestResult = null;
        Verification = ProfileVerification.Unknown;
        Parameters.Clear();
        LaunchCommandPreview = string.Empty;
        AdapterCustomCommand = string.Empty;

        if (!_suppressAdapterSelectionProbe && value is not null && IsOnComponentSetup)
        {
            _ = DetectAdapterCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Latest adapter probe for the selected adapter, or null before it has been probed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyPropertyChangedFor(nameof(IsAdapterMissing))]
    [NotifyPropertyChangedFor(nameof(IsAdapterUsable))]
    [NotifyPropertyChangedFor(nameof(IsAdapterUndetermined))]
    [NotifyPropertyChangedFor(nameof(AdapterProbeDetail))]
    [NotifyPropertyChangedFor(nameof(HasAdapterProbeDetail))]
    [NotifyPropertyChangedFor(nameof(AdapterPackageLocation))]
    [NotifyPropertyChangedFor(nameof(HasAdapterPackageLocation))]
    [NotifyPropertyChangedFor(nameof(IsAdapterBuiltIn))]
    [NotifyPropertyChangedFor(nameof(IsAdapterInstalled))]
    [NotifyPropertyChangedFor(nameof(HasAdapterProbeCommand))]
    // IsAdapterToolchainMissing is gated on IsAdapterMissing, so it changes with the component probe as
    // well as with the toolchain probe. Both notify lists must raise it or the surface it gates goes stale.
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainMissing))]
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainInstallable))]
    [NotifyPropertyChangedFor(nameof(MissingAdapterToolchainName))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainMissingText))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainDocumentation))]
    [NotifyPropertyChangedFor(nameof(StepPositionText))]
    private AcpComponentProbeResult? _adapterProbe;

    /// <summary>
    /// Latest toolchain probe for the selected adapter, or null when it needs none or has not been
    /// probed.
    /// </summary>
    /// <remarks>
    /// Null does not withhold the install offer, for the same reason an undetermined component probe does
    /// not block the walk: an unanswered question is not evidence of absence.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallToolchainCommand))]
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainMissing))]
    [NotifyPropertyChangedFor(nameof(IsAdapterToolchainInstallable))]
    [NotifyPropertyChangedFor(nameof(MissingAdapterToolchainName))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainMissingText))]
    [NotifyPropertyChangedFor(nameof(AdapterToolchainDocumentation))]
    private AcpToolchainProbeResult? _adapterToolchain;

    /// <summary>
    /// A path the user supplied for the adapter's launcher, empty when they supplied none.
    /// </summary>
    /// <remarks>
    /// Separate from the agent row's override because they name different commands: the row overrides the
    /// runtime the agent ships (<c>claude</c>), this overrides the launcher the adapter runs through
    /// (<c>npx</c>). For a packaged adapter that launcher is also the launch plan's executable, so this
    /// is the entry that decides whether a saved profile can start at all.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdapterCustomCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetectAdapterCommand))]
    private string _adapterCustomCommand = string.Empty;

    public bool HasAdapterCustomCommand => !string.IsNullOrWhiteSpace(AdapterCustomCommand);

    partial void OnAdapterCustomCommandChanged(string value)
    {
        TestResult = null;
        Verification = ProfileVerification.Unknown;
        RefreshLaunchCommandPreview();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _profileName = string.Empty;

    /// <summary>Latest end-to-end test result, or null before the first test.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    [NotifyPropertyChangedFor(nameof(IsTestFailed))]
    [NotifyPropertyChangedFor(nameof(TestFailureStageText))]
    [NotifyPropertyChangedFor(nameof(TestRemediationText))]
    [NotifyPropertyChangedFor(nameof(TestErrorDetail))]
    [NotifyPropertyChangedFor(nameof(HasTestErrorDetail))]
    private AcpSetupTestResult? _testResult;

    /// <summary>
    /// Authoritative verification verdict for the draft currently on screen.
    /// </summary>
    /// <remarks>
    /// This is deliberately one value rather than a successful-test flag beside a skipped-test flag. A
    /// draft can have only one verdict, and every plan-changing preparation resets it to
    /// <see cref="ProfileVerification.Unknown"/> before the user can save again.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipTestCommand))]
    [NotifyPropertyChangedFor(nameof(IsTestSuccessful))]
    [NotifyPropertyChangedFor(nameof(IsSkipTestVisible))]
    private ProfileVerification _verification = ProfileVerification.Unknown;

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

    /// <summary>
    /// Human-readable position among the steps that apply to the current draft.
    /// </summary>
    public string StepPositionText => FormatLocalize(
        "AcpSetup_Step_Position",
        "Step {0} of {1}",
        GetApplicableStepPosition(),
        GetApplicableStepCount());

    public bool HasSavedProfile => SavedProfile is not null;

    public bool IsAdapterUsable => AdapterProbe?.IsUsable == true;

    public bool IsAdapterMissing => AdapterProbe?.Availability == AcpComponentAvailability.Missing;

    public bool IsAdapterUndetermined
        => AdapterProbe?.Availability == AcpComponentAvailability.Undetermined;

    /// <summary>
    /// True when the selected adapter is missing and the toolchain that would install it is absent too —
    /// the state where a one-click install cannot succeed and the user needs the toolchain first.
    /// </summary>
    /// <remarks>
    /// Gated on the adapter actually being missing so this never contradicts an installed adapter: an
    /// adapter already present on a machine whose package manager has since gone is usable, and telling
    /// the user to install a toolchain for it would be advice about nothing.
    /// </remarks>
    public bool IsAdapterToolchainMissing
        => IsAdapterMissing
            && SelectedAdapter?.Component.HasAutomaticInstallPath == true
            && AdapterToolchain?.IsMissing == true;

    /// <summary>Name of the adapter's absent toolchain, empty when none is absent.</summary>
    public string MissingAdapterToolchainName
        => IsAdapterToolchainMissing ? AdapterToolchain!.Requirement.DisplayName : string.Empty;

    /// <summary>Documentation for the adapter's absent toolchain, null when none is absent.</summary>
    public Uri? AdapterToolchainDocumentation
        => IsAdapterToolchainMissing ? AdapterToolchain!.Requirement.Documentation : null;

    /// <summary>
    /// True when the wizard can offer to install the adapter's absent toolchain itself.
    /// </summary>
    /// <remarks>
    /// Three conditions, and each rules out a different wrong offer: the toolchain must actually be missing,
    /// this app must publish a source for that particular toolchain, and this platform must be able to run
    /// an installer. A toolchain with no source keeps the documentation link — the state that made the whole
    /// step a dead end before, since the wizard's advice was to leave and come back.
    /// </remarks>
    public bool IsAdapterToolchainInstallable
        => IsAdapterToolchainMissing
            && AdapterToolchain!.Requirement.HasAutomaticInstallPath
            && _orchestrator.SupportsToolchainInstall;

    /// <summary>
    /// Localized sentence naming the toolchain the adapter install needs, empty when it is present.
    /// </summary>
    /// <remarks>
    /// Composed here rather than through the view's <c>x:Uid</c> because it interpolates the toolchain
    /// name, and a <c>Message</c> assigned from a resw would overwrite the binding.
    /// </remarks>
    public string AdapterToolchainMissingText
        => IsAdapterToolchainMissing
            ? FormatLocalize(
                AdapterToolchainMissingKey,
                "{0} is required before this adapter can be installed. Install it, then detect again.",
                MissingAdapterToolchainName)
            : string.Empty;

    /// <summary>
    /// Diagnostic detail from the adapter probe, empty when it reported none.
    /// </summary>
    /// <remarks>
    /// The probe records why it reached its verdict — a missing launcher, an unsupported platform — and
    /// that sentence is the difference between a red mark the user can act on and one they cannot. It is
    /// shown alongside the state's own copy, never as the primary message, because the detail is
    /// developer-facing English from the platform layer rather than localized guidance.
    /// </remarks>
    public string AdapterProbeDetail => AdapterProbe?.Detail ?? string.Empty;

    /// <summary>Package directory reported by the package manager when the adapter was found.</summary>
    public string AdapterPackageLocation => AdapterProbe?.PackageLocation ?? string.Empty;

    public bool HasAdapterPackageLocation => !string.IsNullOrWhiteSpace(AdapterPackageLocation);

    /// <summary>
    /// The launcher the adapter is detected and started through, empty for a built-in adapter that needs
    /// no external command.
    /// </summary>
    public string AdapterProbeCommand => SelectedAdapter?.Component.ProbeCommand ?? string.Empty;

    /// <summary>
    /// True when the selected adapter ships inside the agent, so nothing has to be installed or found.
    /// </summary>
    /// <remarks>
    /// Distinguished from a merely usable adapter so the step can say why it is already satisfied. Most
    /// of the catalog is built-in, and without this the step rendered a title and an otherwise empty
    /// panel: correct, but indistinguishable from a page that failed to load.
    /// </remarks>
    public bool IsAdapterBuiltIn
        => AdapterProbe?.Availability == AcpComponentAvailability.BuiltIn;

    /// <summary>True when the adapter is present as a separate component the user had to have.</summary>
    public bool IsAdapterInstalled
        => AdapterProbe?.Availability == AcpComponentAvailability.Installed;

    /// <summary>
    /// True when the custom-command override panel is worth showing: the launcher is named and the
    /// probe did not already confirm the adapter present. An installed adapter has a launcher too,
    /// so gating on the command alone would offer "why not found?" about a component that was found.
    /// </summary>
    public bool HasAdapterProbeCommand
        => !string.IsNullOrWhiteSpace(AdapterProbeCommand) && !IsAdapterUsable;

    public bool HasAdapterProbeDetail => !string.IsNullOrWhiteSpace(AdapterProbeDetail);

    public bool HasTestResult => TestResult is not null;

    public bool IsTestSuccessful => Verification.IsVerified;

    /// <summary>True while the optional test may still be deliberately skipped.</summary>
    public bool IsSkipTestVisible => IsOnTest && !IsTestSuccessful;

    /// <summary>True when the last test failed. Distinct from HasTestResult, which is also
    /// true on success — the failure banner must not open on a passing test.</summary>
    public bool IsTestFailed => TestResult is not null && !TestResult.IsSuccess;

    /// <summary>Raw failure detail from the connectivity test (stderr excerpt, protocol error),
    /// empty on success or before any test.</summary>
    /// <remarks>
    /// Developer-facing English from the platform layer, shown as the small print under the
    /// localized remediation advice — same contract as AdapterProbeDetail.
    /// </remarks>
    public string TestErrorDetail => TestResult?.ErrorDetail ?? string.Empty;

    public bool HasTestErrorDetail => !string.IsNullOrWhiteSpace(TestErrorDetail);

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

    /// <summary>
    /// Probes every catalog agent, searching the machine afresh.
    /// </summary>
    /// <remarks>
    /// The search is invalidated first because this command is the button the wizard points a user at after
    /// telling them to install a missing toolchain. Reusing the cached search would answer about the machine
    /// as it was before they did so, and report the toolchain still absent no matter how many times they
    /// pressed it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task DetectAgentsAsync(CancellationToken cancellationToken)
    {
        await RunOperationAsync(
            async token =>
            {
                _orchestrator.InvalidateSearchPaths();
                MarkAgentsChecking();
                var states = await _orchestrator
                    .DetectAgentsAsync(CollectCommandOverrides(), token)
                    .ConfigureAwait(false);
                await _uiDispatcher.EnqueueAsync(() => ApplyRuntimeProbes(states)).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-probes one row after the user supplied a path for it.
    /// </summary>
    /// <remarks>
    /// Scoped to the single row so confirming one path does not relaunch a process for every catalog
    /// agent, and so the answer the user gets is unambiguously about the path they just typed.
    /// </remarks>
    private void VerifyAgentRow(AcpSetupAgentRowViewModel row)
    {
        if (IsBusy)
        {
            return;
        }

        _ = RunOperationAsync(
            async token =>
            {
                var probe = await _orchestrator
                    .DetectComponentAsync(row.Agent.Runtime, CollectCommandOverrides(), token)
                    .ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        row.Runtime = probe;
                        OnPropertyChanged(nameof(StepPositionText));
                    })
                    .ConfigureAwait(false);
            },
            CancellationToken.None);
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
                // Same reason as DetectAgentsAsync: this command backs the "detect again" button the
                // toolchain-missing surface offers, so it must look at the machine rather than at the
                // search that was current when the wizard first said the toolchain was absent.
                _orchestrator.InvalidateSearchPaths();
                var overrides = CollectCommandOverrides();
                var probe = await _orchestrator
                    .DetectComponentAsync(adapter.Component, overrides, token)
                    .ConfigureAwait(false);

                // Probed in the same operation as the component: the install button this gates appears on
                // the same render as the "missing" verdict that reveals it, so it is never briefly offered
                // on a machine that cannot honour it.
                var toolchain = await _orchestrator
                    .DetectToolchainAsync(adapter.Component, overrides, token)
                    .ConfigureAwait(false);

                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        // A user can change the native ComboBox while the process probe is running.
                        // The late result belongs to the captured adapter, never automatically to the
                        // newer selection.
                        if (ReferenceEquals(SelectedAdapter, adapter))
                        {
                            AdapterProbe = probe;
                            AdapterToolchain = toolchain;
                        }
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanDetectAdapter() => !IsBusy && SelectedAdapter is not null;

    // ── Installation ────────────────────────────────────────────────────────

    /// <summary>
    /// Installs one agent's runtime on that row's own request. This is the only runtime-install entry
    /// point: the row's button raises <see cref="AcpSetupAgentRowViewModel.InstallRequested"/> rather
    /// than binding a command, because the button lives in an ItemTemplate and acts on its own item.
    /// </summary>
    /// <remarks>
    /// Both capability checks belong here rather than in the row's button visibility. The row can only
    /// see whether its own component is installable; whether this platform installs anything at all is
    /// the orchestrator's answer, and a platform that declines every request would otherwise be handed
    /// one and report its refusal as a failure the user is invited to retry.
    ///
    /// <see cref="AcpSetupAgentRowViewModel.CanInstallHere"/> covers the machine's own toolchain, so a
    /// row whose package manager is absent is not handed a request that could only fail. This is restated
    /// here rather than trusted to the button's visibility because the button raises an event rather than
    /// binding a command, and an event has no CanExecute to consult.
    /// </remarks>
    private void InstallAgentRow(AcpSetupAgentRowViewModel row)
    {
        if (IsBusy || !SupportsAutomaticInstall || !row.CanInstallHere)
        {
            return;
        }

        // Each install starts from its own log. The output surface is shared by every install entry point,
        // so without this a row install would open showing the lines a previous component produced,
        // attributed to this one. Cleared before the operation starts so the panel is empty for the whole of
        // it, rather than showing the previous log until the first line of this one arrives.
        ResetInstallOutput();

        _ = RunOperationAsync(
            async token =>
            {
                var overrides = CollectCommandOverrides();
                var (install, probe) = await _orchestrator
                    .InstallComponentAsync(row.Agent.Runtime, AppendInstallOutput, overrides, token)
                    .ConfigureAwait(false);

                // Re-probed after the attempt because a failed install is evidence about the toolchain
                // too: the manager may have disappeared since the sweep, and leaving the stale verdict
                // would keep offering a button that just failed.
                var toolchain = await _orchestrator
                    .DetectToolchainAsync(row.Agent.Runtime, overrides, token)
                    .ConfigureAwait(false);

                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        row.Runtime = probe;
                        row.RuntimeToolchain = toolchain;
                        OnPropertyChanged(nameof(StepPositionText));
                        ReportInstallFailure(install);
                    })
                    .ConfigureAwait(false);
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Installs the missing package-manager toolchain for one agent row. Like component installation, this
    /// starts from the row's event because an ItemTemplate action has no command parameter of its own.
    /// </summary>
    private void InstallAgentToolchain(AcpSetupAgentRowViewModel row)
    {
        if (IsBusy || !row.CanInstallToolchainHere || !_orchestrator.SupportsToolchainInstall)
        {
            return;
        }

        ResetInstallOutput();
        _ = RunOperationAsync(
            async token =>
            {
                var overrides = CollectCommandOverrides();
                var (install, toolchain) = await _orchestrator
                    .InstallToolchainAsync(row.Agent.Runtime, AppendInstallOutput, overrides, token)
                    .ConfigureAwait(false);

                await _uiDispatcher.EnqueueAsync(() =>
                {
                    row.RuntimeToolchain = toolchain;
                    ReportToolchainInstallFailure(install);
                }).ConfigureAwait(false);
            },
            CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanInstallToolchain))]
    private async Task InstallToolchainAsync(CancellationToken cancellationToken)
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
        {
            return;
        }

        ResetInstallOutput();
        await RunOperationAsync(
            async token =>
            {
                var overrides = CollectCommandOverrides();
                var (install, toolchain) = await _orchestrator
                    .InstallToolchainAsync(adapter.Component, AppendInstallOutput, overrides, token)
                    .ConfigureAwait(false);

                await _uiDispatcher.EnqueueAsync(() =>
                {
                    // A ComboBox change can land while the downloader is running; never apply an old
                    // adapter's result to the newly selected one.
                    if (ReferenceEquals(SelectedAdapter, adapter))
                    {
                        AdapterToolchain = toolchain;
                        ReportToolchainInstallFailure(install);
                    }
                }).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanInstallToolchain()
        => !IsBusy && IsAdapterToolchainInstallable;

    [RelayCommand(CanExecute = nameof(CanInstallAdapter))]
    private async Task InstallAdapterAsync(CancellationToken cancellationToken)
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
        {
            return;
        }

        // Same reason as the row install: one shared surface, so each install starts from its own log.
        ResetInstallOutput();

        await RunOperationAsync(
            async token =>
            {
                var overrides = CollectCommandOverrides();
                var (install, probe) = await _orchestrator
                    .InstallComponentAsync(adapter.Component, AppendInstallOutput, overrides, token)
                    .ConfigureAwait(false);
                var toolchain = await _orchestrator
                    .DetectToolchainAsync(adapter.Component, overrides, token)
                    .ConfigureAwait(false);

                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        if (ReferenceEquals(SelectedAdapter, adapter))
                        {
                            AdapterProbe = probe;
                            AdapterToolchain = toolchain;
                            ReportInstallFailure(install);
                        }
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Installing the adapter needs three things to hold: the platform runs installers, the component has
    /// an install command, and this machine has the toolchain to run it.
    /// </summary>
    /// <remarks>
    /// A toolchain probe that came back undetermined does not withhold the offer — the installer's own
    /// report is a better answer than refusing over a question the probe could not settle.
    /// </remarks>
    private bool CanInstallAdapter()
        => !IsBusy
            && SelectedAdapter is not null
            && SupportsAutomaticInstall
            && SelectedAdapter.Component.HasAutomaticInstallPath
            && AdapterToolchain?.IsMissing != true;

    // ── Test and save ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        var draft = BuildDraft();
        if (draft is null)
        {
            return;
        }

        // A previous passing result belongs to the previous test run. Clear its verdict before starting
        // another attempt so cancellation, failure, or an exception can never leave stale proof attached to
        // the draft.
        TestResult = null;
        Verification = ProfileVerification.Unknown;

        await RunOperationAsync(
            async token =>
            {
                var result = await _orchestrator.TestDraftAsync(draft, token).ConfigureAwait(false);
                await _uiDispatcher
                    .EnqueueAsync(() =>
                    {
                        TestResult = result;
                        Verification = result.IsSuccess
                            ? ProfileVerification.Verified(_timeProvider.GetUtcNow())
                            : ProfileVerification.Unknown;
                        ApplyValidationMessages(result);
                    })
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool CanTest() => !IsBusy && SelectedAdapter is not null;

    /// <summary>
    /// Deliberately accepts an untested draft and moves to naming it. This is distinct from automatic step
    /// folding: folding omits a step that has no work, while this command records a user decision with a
    /// durable <see cref="ProfileVerification.Unverified"/> verdict.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSkipTest))]
    private void SkipTest()
    {
        if (!CanSkipTest())
        {
            return;
        }

        Verification = ProfileVerification.Unverified;
        PrepareSave();
        Step = AcpSetupWizardStep.Save;
    }

    private bool CanSkipTest()
        => !IsBusy
            && IsOnTest
            && !IsTestSuccessful
            && SelectedAgent is not null
            && SelectedAdapter is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // RelayCommand invocation does not consult CanExecute, so the rule is restated here. The draft must
        // carry either a passing test verdict or the explicit decision made by SkipTest; Unknown is never a
        // saveable answer.
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

    /// <summary>Saving requires an explicit verification verdict and the dedicated save step.</summary>
    private bool CanSave()
        => !IsBusy
            && IsOnSave
            && Verification.State != ProfileVerificationState.Unknown
            && SelectedAdapter is not null
            && !string.IsNullOrWhiteSpace(ProfileName);

    // ── Step machine ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoNextAsync(CancellationToken cancellationToken)
    {
        // AsyncRelayCommand can still be invoked directly while CanExecute is false. Keep the transition
        // guard inside the handler so a missing runtime/adapter cannot be bypassed by a stale binding.
        if (!CanGoNext())
        {
            return;
        }

        switch (Step)
        {
            case AcpSetupWizardStep.AgentSelection:
                PrepareComponentSetup();
                await DetectAdapterAsync(cancellationToken).ConfigureAwait(false);

                // Detection is preparation, not presentation. Only a conclusively no-op component step is
                // folded; a missing, undetermined, failed, or cancelled probe remains visible and keeps the
                // same blocking rule as an ordinary visit to that step.
                if (IsStepApplicable(AcpSetupWizardStep.ComponentSetup)
                    || !CanAdvanceFromComponentSetup())
                {
                    Step = AcpSetupWizardStep.ComponentSetup;
                    break;
                }

                AdvancePastComponentSetup();
                break;
            case AcpSetupWizardStep.ComponentSetup:
                AdvancePastComponentSetup();
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
        AcpSetupWizardStep.ComponentSetup => CanAdvanceFromComponentSetup(),
        AcpSetupWizardStep.Parameters => true,
        AcpSetupWizardStep.Test => IsTestSuccessful,
        _ => false
    };

    /// <summary>
    /// The component gate shared by a rendered visit and an automatically folded visit. Keeping one gate is
    /// what prevents folding from becoming a second, weaker route around a missing runtime or adapter.
    /// </summary>
    private bool CanAdvanceFromComponentSetup()
        => SelectedAdapter is not null
            && SelectedAgent?.IsMissing != true
            && AdapterProbe is not null
            && !IsAdapterMissing;

    private void AdvancePastComponentSetup()
    {
        PrepareParameters();
        if (IsStepApplicable(AcpSetupWizardStep.Parameters))
        {
            Step = AcpSetupWizardStep.Parameters;
            return;
        }

        // A hidden Parameters step still owns default-value projection and validation. Preparing it above
        // keeps the launch plan identical to the one produced when the same template has a visible form.
        if (TryPrepareTest())
        {
            Step = AcpSetupWizardStep.Test;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        var previous = FindPreviousApplicableStep();
        if (IsBusy || previous is null)
        {
            return;
        }

        Step = previous.Value;
        ErrorMessage = string.Empty;
    }

    private bool CanGoBack() => !IsBusy && FindPreviousApplicableStep() is not null;

    /// <summary>
    /// Returns whether a step has user-visible work for the current draft. No skipped-step collection is
    /// stored: applicability is derived from the authoritative catalog, selection, and probe facts every
    /// time, so changing an adapter cannot leave stale navigation state behind.
    /// </summary>
    private bool IsStepApplicable(AcpSetupWizardStep step) => step switch
    {
        AcpSetupWizardStep.ComponentSetup => IsComponentSetupApplicable(),
        AcpSetupWizardStep.Parameters => IsParametersStepApplicable(),
        _ => true
    };

    private bool IsComponentSetupApplicable()
    {
        var agentRow = SelectedAgent;
        if (agentRow is null || agentRow.IsMissing)
        {
            return true;
        }

        var agent = agentRow.Agent;
        var adapter = SelectedAdapter ?? agent.ResolveRecommendedAdapter();
        if (adapter is null || agent.Adapters.Count != 1)
        {
            // Selecting among alternatives is work even when the recommended adapter itself is built in.
            return true;
        }

        if (SelectedAdapter is null)
        {
            // Before entering the walk, the catalog can already prove that a sole built-in adapter needs no
            // setup. External adapters wait for their probe, whose result may carry actionable diagnostics.
            return adapter.Component.Distribution != AcpDistributionKind.BuiltIn;
        }

        // Once prepared, only a positive BuiltIn verdict is safe to fold. Null/Undetermined remains visible;
        // "could not tell" is not the same fact as "nothing to do".
        return AdapterProbe?.Availability != AcpComponentAvailability.BuiltIn;
    }

    private bool IsParametersStepApplicable()
    {
        var adapter = SelectedAdapter ?? SelectedAgent?.Agent.ResolveRecommendedAdapter();
        // Before an agent is selected, keep the complete five-step shape. Once selected, the launch
        // template itself is authoritative about whether a form exists.
        return adapter is null || adapter.LaunchTemplate.Parameters.Count > 0;
    }

    private AcpSetupWizardStep? FindPreviousApplicableStep()
    {
        for (var value = (int)Step - 1; value >= (int)AcpSetupWizardStep.AgentSelection; value--)
        {
            var candidate = (AcpSetupWizardStep)value;
            if (IsStepApplicable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private int GetApplicableStepPosition()
    {
        var position = 0;
        for (var value = (int)AcpSetupWizardStep.AgentSelection; value <= (int)Step; value++)
        {
            if (IsStepApplicable((AcpSetupWizardStep)value))
            {
                position++;
            }
        }

        return Math.Max(position, 1);
    }

    private int GetApplicableStepCount()
    {
        var count = 0;
        for (var value = (int)AcpSetupWizardStep.AgentSelection;
             value <= (int)AcpSetupWizardStep.Save;
             value++)
        {
            if (IsStepApplicable((AcpSetupWizardStep)value))
            {
                count++;
            }
        }

        return count;
    }

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

        _suppressAdapterSelectionProbe = true;
        try
        {
            SelectedAdapter = agent.ResolveRecommendedAdapter();
        }
        finally
        {
            _suppressAdapterSelectionProbe = false;
        }
    }

    private void PrepareParameters()
    {
        Parameters.Clear();
        TestResult = null;
        Verification = ProfileVerification.Unknown;
        var template = SelectedAdapter?.LaunchTemplate;
        if (template is null)
        {
            LaunchCommandPreview = string.Empty;
            return;
        }

        foreach (var definition in template.Parameters)
        {
            Parameters.Add(
                new AcpSetupParameterRowViewModel(definition, OnParameterValueChanged, _localizer));
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
        Verification = ProfileVerification.Unknown;
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

    private void OnParameterValueChanged()
    {
        TestResult = null;
        Verification = ProfileVerification.Unknown;
        RefreshLaunchCommandPreview();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects authoritative command paths, keyed by the catalog command each one replaces.
    /// </summary>
    /// <remarks>
    /// User choices outrank probe results. Otherwise, when an adapter launches the command it probed, the
    /// resolved executable path is carried into the launch plan automatically. Search-path widening can
    /// find an install outside the GUI process PATH; saving the bare command after that would discard the
    /// fact detection just established and produce a profile that cannot start.
    /// </remarks>
    private AcpCommandOverrides CollectCommandOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Agents)
        {
            if (row.HasCustomCommand)
            {
                overrides[row.ProbeCommand] = row.CustomCommand;
            }
        }

        if (!string.IsNullOrWhiteSpace(AdapterProbeCommand)
            && string.Equals(
                SelectedAdapter?.LaunchTemplate.Command,
                AdapterProbeCommand,
                StringComparison.Ordinal))
        {
            var resolvedAdapterCommand = HasAdapterCustomCommand
                ? AdapterCustomCommand
                : AdapterProbe?.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(resolvedAdapterCommand))
            {
                overrides[AdapterProbeCommand] = resolvedAdapterCommand;
            }
        }

        return AcpCommandOverrides.Create(overrides);
    }

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
            CommandOverrides = CollectCommandOverrides(),
            ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? agent.DisplayName : ProfileName.Trim(),
            Verification = Verification
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
                .Build(template, CollectParameterValues(), CollectCommandOverrides())
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

    /// <remarks>
    /// The toolchain verdict is written before the component probe. Both feed the row's install offer, and
    /// the component probe is what raises the flags the view reads — so writing it last means the view
    /// never renders a "missing" row against a toolchain answer from the previous sweep.
    /// </remarks>
    private void ApplyRuntimeProbes(IReadOnlyList<AcpAgentDetectionState> states)
    {
        var byAgentId = new Dictionary<string, AcpAgentDetectionState>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            byAgentId[state.Agent.Id] = state;
        }

        foreach (var row in Agents)
        {
            if (byAgentId.TryGetValue(row.AgentId, out var state))
            {
                row.RuntimeToolchain = state.RuntimeToolchain;
                row.Runtime = state.Runtime;
            }
        }

        OnPropertyChanged(nameof(StepPositionText));
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

    /// <summary>
    /// Clears installer output so a new component's install does not inherit the last one's.
    /// </summary>
    /// <remarks>
    /// Marshalled like <see cref="AppendInstallOutput"/> rather than mutating the collection directly. This
    /// collection is bound to a list, so a change raised off the UI thread is a crash on WinUI rather than a
    /// glitch — and the callers are no longer all on that thread now that both install entry points clear
    /// before starting. Routing it here means the guarantee holds wherever it is called from, instead of
    /// resting on each call site sitting in the right place.
    /// </remarks>
    private void ResetInstallOutput()
    {
        _uiDispatcher.Enqueue(() =>
        {
            InstallOutput.Clear();
            NotifyInstallOutputChanged();
        });
    }

    private void NotifyInstallOutputChanged()
    {
        OnPropertyChanged(nameof(LatestInstallOutputLine));
        OnPropertyChanged(nameof(HasInstallOutput));
    }

    private void ReportToolchainInstallFailure(AcpToolchainInstallResult install)
    {
        if (!install.IsSuccess)
        {
            ErrorMessage = !string.IsNullOrEmpty(install.RemediationKey)
                ? Localize(
                    install.RemediationKey!,
                    "Could not install the required toolchain. Use the documentation link to install it manually.")
                : install.ErrorDetail ?? Localize(InstallFailedKey, "Installation failed.");
            return;
        }

        // PATH registration makes future terminals convenient, but it does not decide whether Node is
        // installed — the just-completed re-probe does. Tell the user about this partial result without
        // turning a working setup into an error state.
        if (install.PathRegistration == AcpPathRegistration.Failed)
        {
            ErrorMessage = Localize(
                ToolchainPathRegistrationFailedKey,
                "The toolchain was installed, but PATH could not be updated. New terminals may need manual PATH configuration.");
        }
    }

    /// <summary>
    /// Surfaces a failed install, preferring localized advice over the platform layer's raw detail.
    /// </summary>
    /// <remarks>
    /// A remediation key wins over <see cref="AcpComponentInstallResult.ErrorDetail"/> because the detail
    /// is untranslated diagnostics naming an executable, while the key resolves to the thing the user has
    /// to do. The detail is not lost — the installer output surface still carries it.
    /// </remarks>
    private void ReportInstallFailure(AcpComponentInstallResult install)
    {
        if (install.IsSuccess)
        {
            return;
        }

        if (!string.IsNullOrEmpty(install.RemediationKey))
        {
            ErrorMessage = FormatLocalize(
                install.RemediationKey!,
                "{0} is required to install this component. Install it, then run detection again.",
                install.MissingToolchainName ?? string.Empty);
            return;
        }

        ErrorMessage = install.ErrorDetail ?? Localize(InstallFailedKey, "Installation failed.");
    }

    private bool CanRunOperation() => !IsBusy;

    /// <summary>
    /// Asks the running operation to stop.
    /// </summary>
    /// <remarks>
    /// Needed most by installs, which are the wizard's one genuinely long operation: a package manager
    /// fetching over a slow network can sit for minutes, and the guard against that hanging forever is a
    /// ten-minute timeout. Without a way out, a user who started the wrong install, or whose network
    /// stalled, could only wait it out or kill the app.
    ///
    /// Cancelling is not the same as undoing. The package manager is killed with its process tree, which
    /// leaves whatever it had already written on disk — so the re-probe after the operation is what says
    /// where the component actually stands, exactly as it does for an install that ran to completion.
    ///
    /// Placed on the shared operation scope rather than per command because one entry point has no command
    /// to cancel: an agent row raises an event, so a per-command token would leave the row's install — the
    /// most likely one to need stopping — the only one that could not be.
    ///
    /// A no-op once the operation has released its cancellation source, which is briefly true while the busy
    /// flag is still on its way to the UI thread. Doing nothing is the right answer there: the work being
    /// cancelled has already finished.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    /// <summary>
    /// Cancellation source for the operation in flight, or null when none is running.
    /// </summary>
    /// <remarks>
    /// Single because <see cref="RunOperationAsync"/> admits one operation at a time; the busy flag it
    /// checks is what makes that true.
    /// </remarks>
    private CancellationTokenSource? _operationCancellation;

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

        // Linked so both routes into cancellation work: the caller's own token — a command's token, or
        // CancellationToken.None from an event-raised install — and the user pressing cancel.
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await operation(cancellation.Token).ConfigureAwait(false);
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
            // Released before it is disposed, and disposed only after the busy flag has been cleared.
            // The flag is marshalled to the UI thread, so it lands after this method has moved on: that
            // leaves a window where the button is still enabled with nothing left to cancel. Clearing the
            // field first makes a click in that window a no-op — the alternative order leaves the field
            // pointing at a disposed source, where Cancel throws ObjectDisposedException inside a command
            // handler.
            _operationCancellation = null;
            await _uiDispatcher.EnqueueAsync(() => IsBusy = false).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private string Localize(string key, string fallback)
        => CoreStringResolver.Resolve(_localizer, key, fallback);

    private string FormatLocalize(string key, string fallbackFormat, params object[] arguments)
        => CoreStringResolver.ResolveFormat(_localizer, key, fallbackFormat, arguments);

    private const string InstallFailedKey = "AcpSetup_Install_Failed";

    private const string ToolchainPathRegistrationFailedKey = "AcpSetup_Toolchain_PathRegistrationFailed";

    private const string AdapterToolchainMissingKey = "AcpSetup_Adapter_ToolchainMissing";

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

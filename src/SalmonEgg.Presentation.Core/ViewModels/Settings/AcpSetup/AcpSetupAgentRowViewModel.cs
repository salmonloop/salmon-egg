using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

/// <summary>
/// One catalog agent as the selection step shows it: the descriptor plus what detection learned about
/// the runtime on this machine.
/// </summary>
/// <remarks>
/// Availability is projected as separate booleans rather than one status string so the view can style
/// each state without a converter, and so "undetermined" stays visibly distinct from "missing" — the
/// wizard must never tell a user to install something it merely failed to look for.
/// </remarks>
public sealed partial class AcpSetupAgentRowViewModel : ObservableObject
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public AcpSetupAgentRowViewModel(
        AcpAgentDescriptor agent,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _localizer = localizer;
        _runtime = AcpComponentProbeResult.Undetermined(agent.Runtime.Id);
    }

    public AcpAgentDescriptor Agent { get; }

    public string AgentId => Agent.Id;

    public string DisplayName => Agent.DisplayName;

    /// <summary>
    /// The agent's one-line description, already localized.
    /// </summary>
    /// <remarks>
    /// The descriptor carries a resource key, not display text. Binding a view straight to that key
    /// puts the key on screen, so resolution happens here rather than in the view: these keys live only
    /// in this assembly's CoreStrings resources, which the UI layer's own <c>x:Uid</c> pipeline cannot
    /// reach. Falls back to the key when no localizer is supplied, matching the wizard's own Localize
    /// contract, so a missing resource degrades to a diagnosable string instead of an empty row.
    /// </remarks>
    public string Description => CoreStringResolver.Resolve(_localizer, Agent.Description, Agent.Description);

    /// <summary>Latest runtime probe. Replaced wholesale so every derived flag updates together.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Availability))]
    [NotifyPropertyChangedFor(nameof(IsInstalled))]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(IsUndetermined))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(Version))]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    [NotifyPropertyChangedFor(nameof(ProbeDetail))]
    [NotifyPropertyChangedFor(nameof(HasProbeDetail))]
    [NotifyPropertyChangedFor(nameof(ResolvedPath))]
    [NotifyPropertyChangedFor(nameof(HasResolvedPath))]
    private AcpComponentProbeResult _runtime;

    public AcpComponentAvailability Availability => Runtime.Availability;

    public bool IsInstalled => Runtime.IsUsable;

    public bool IsMissing => Runtime.Availability == AcpComponentAvailability.Missing;

    public bool IsUndetermined => Runtime.Availability == AcpComponentAvailability.Undetermined;

    public bool IsChecking => Runtime.Availability == AcpComponentAvailability.Checking;

    public string Version => Runtime.Version ?? string.Empty;

    public bool HasVersion => !string.IsNullOrWhiteSpace(Runtime.Version);

    /// <summary>True when this agent can be installed by the wizard rather than by hand.</summary>
    public bool SupportsAutomaticInstall => Agent.Runtime.SupportsAutomaticInstall;

    /// <summary>Documentation to offer when automatic installation is unavailable or fails.</summary>
    public Uri? InstallDocumentation => Agent.Runtime.InstallDocumentation;

    /// <summary>The command name the wizard probes for, so the row can say what it looked for.</summary>
    public string ProbeCommand => Agent.Runtime.ProbeCommand;

    /// <summary>Diagnostic detail from the last probe, empty when it reported none.</summary>
    public string ProbeDetail => Runtime.Detail ?? string.Empty;

    public bool HasProbeDetail => !string.IsNullOrWhiteSpace(ProbeDetail);

    /// <summary>Absolute path where the probe found the runtime, empty when it did not.</summary>
    public string ResolvedPath => Runtime.ExecutablePath ?? string.Empty;

    public bool HasResolvedPath => !string.IsNullOrWhiteSpace(ResolvedPath);

    /// <summary>
    /// A path the user supplied for <see cref="ProbeCommand"/>, empty when they supplied none.
    /// </summary>
    /// <remarks>
    /// Editing this does not re-probe. Probing costs a process launch, so it happens when the user asks
    /// through <see cref="RequestVerify"/> rather than once per keystroke.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomCommand))]
    private string _customCommand = string.Empty;

    public bool HasCustomCommand => !string.IsNullOrWhiteSpace(CustomCommand);

    /// <summary>
    /// Raised when the user asks this row to be installed; the owning wizard subscribes because it
    /// owns the busy flag and the error surface. Null when nobody is listening.
    /// </summary>
    public event Action<AcpSetupAgentRowViewModel>? InstallRequested;

    public void RequestInstall() => InstallRequested?.Invoke(this);

    /// <summary>
    /// Raised when the user asks for this row to be probed again, after supplying a custom path.
    /// </summary>
    public event Action<AcpSetupAgentRowViewModel>? VerifyRequested;

    public void RequestVerify() => VerifyRequested?.Invoke(this);
}

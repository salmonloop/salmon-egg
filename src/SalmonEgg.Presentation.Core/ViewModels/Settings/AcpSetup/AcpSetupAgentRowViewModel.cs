using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models.AcpSetup;

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
    public AcpSetupAgentRowViewModel(AcpAgentDescriptor agent)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _runtime = AcpComponentProbeResult.Undetermined(agent.Runtime.Id);
    }

    public AcpAgentDescriptor Agent { get; }

    public string AgentId => Agent.Id;

    public string DisplayName => Agent.DisplayName;

    /// <summary>Localization key for the agent's one-line description.</summary>
    public string DescriptionKey => Agent.Description;

    /// <summary>Latest runtime probe. Replaced wholesale so every derived flag updates together.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Availability))]
    [NotifyPropertyChangedFor(nameof(IsInstalled))]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(IsUndetermined))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(Version))]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
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

    /// <summary>
    /// Raised when the user asks this row to be installed; the owning wizard subscribes because it
    /// owns the busy flag and the error surface. Null when nobody is listening.
    /// </summary>
    public event Action<AcpSetupAgentRowViewModel>? InstallRequested;

    public void RequestInstall() => InstallRequested?.Invoke(this);
}

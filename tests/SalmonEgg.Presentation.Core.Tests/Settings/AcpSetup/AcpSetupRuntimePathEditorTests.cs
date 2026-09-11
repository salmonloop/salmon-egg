using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupRuntimePathEditorTests
{
    [Fact]
    public void CustomCommand_TypedAndCleared_KeepsEditorAvailableWithoutAnOldProbeVerdict()
    {
        // Arrange
        var row = new AcpSetupAgentRowViewModel(AcpSetupWizardFixtures.Agent());
        Assert.False(row.IsRuntimePathEditorVisible);
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        row.Runtime = AcpComponentProbeResult.Missing(row.Agent.Runtime.Id);
        Assert.True(row.IsRuntimePathEditorVisible);
        Assert.Contains(nameof(row.IsRuntimePathEditorVisible), changed);

        // Act / Assert: every keystroke and clearing the input leave the same editor reachable.
        foreach (var text in new[] { "/", "/c", "/custom", "/custom/bin/agent", string.Empty, " ", "/replacement" })
        {
            changed.Clear();
            row.CustomCommand = text;

            Assert.True(row.IsUndetermined);
            Assert.False(row.IsMissing);
            Assert.True(row.IsRuntimePathEditorVisible);
            Assert.Contains(nameof(row.IsRuntimePathEditorVisible), changed);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Runtime_CustomPathProbeCompletes_KeepsTheSelectedPathEditable(bool found)
    {
        // Arrange
        var row = new AcpSetupAgentRowViewModel(AcpSetupWizardFixtures.Agent());
        row.Runtime = AcpComponentProbeResult.Missing(row.Agent.Runtime.Id);
        row.CustomCommand = "/custom/bin/agent";

        // Act
        row.Runtime = found
            ? AcpComponentProbeResult.Installed(row.Agent.Runtime.Id, row.CustomCommand, version: null)
            : AcpComponentProbeResult.Missing(row.Agent.Runtime.Id);

        // Assert
        Assert.Equal(found, row.IsInstalled);
        Assert.Equal(!found, row.IsMissing);
        Assert.True(row.IsRuntimePathEditorVisible);
        Assert.Equal("/custom/bin/agent", row.CustomCommand);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Runtime_DefaultPathProbeCompletesAfterClearing_UsesTheNewVerdict(bool found)
    {
        // Arrange
        var row = new AcpSetupAgentRowViewModel(AcpSetupWizardFixtures.Agent());
        row.CustomCommand = "/custom/bin/agent";
        row.CustomCommand = string.Empty;
        Assert.True(row.IsRuntimePathEditorVisible);
        var notified = false;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(row.IsRuntimePathEditorVisible))
            {
                notified = true;
            }
        };

        // Act
        row.Runtime = found
            ? AcpComponentProbeResult.Installed(row.Agent.Runtime.Id, "/default/bin/agent", version: null)
            : AcpComponentProbeResult.Missing(row.Agent.Runtime.Id);

        // Assert
        Assert.True(notified);
        Assert.Equal(!found, row.IsRuntimePathEditorVisible);
        Assert.False(row.HasCustomCommand);
    }

    [Fact]
    public void SelectedCandidate_ChangedBeforeProbeCompletes_PreservesCandidatesAndUsesTheChosenPath()
    {
        // Arrange
        var row = new AcpSetupAgentRowViewModel(AcpSetupWizardFixtures.Agent());
        string[] candidates = ["/first/bin/agent", "/second/bin/agent"];
        row.Runtime = AcpComponentProbeResult.Installed(
            row.Agent.Runtime.Id, candidates[0], version: null, candidates: candidates);
        var requestedPaths = new List<string>();
        row.VerifyRequested += selected => requestedPaths.Add(selected.CustomCommand);

        // Act
        row.SelectedCandidate = candidates[1];

        // Assert
        Assert.True(row.IsUndetermined);
        Assert.True(row.IsRuntimePathEditorVisible);
        Assert.True(row.HasMultipleCandidates);
        Assert.Equal(candidates, row.Candidates);
        Assert.Equal(candidates[1], row.SelectedCandidate);
        Assert.Equal(candidates[1], Assert.Single(requestedPaths));
    }
}

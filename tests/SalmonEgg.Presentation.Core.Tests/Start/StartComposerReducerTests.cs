using SalmonEgg.Presentation.ViewModels.Start;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Start;

public sealed class StartComposerReducerTests
{
    [Fact]
    public void Reduce_Loaded_ProjectsCollapsedSnapshot()
    {
        // Arrange
        var state = StartComposerState.Default;

        // Act
        var next = StartComposerReducer.Reduce(state, new Loaded());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.Collapsed, snapshot.Stage);
        Assert.False(snapshot.IsExpanded);
        Assert.True(snapshot.ShowHeroSuggestions);
        Assert.False(snapshot.ShowPreflightSuggestions);
    }

    [Fact]
    public void Reduce_FocusEntered_ExpandsComposer()
    {
        // Arrange
        var state = StartComposerState.Default;

        // Act
        var next = StartComposerReducer.Reduce(state, new FocusEntered());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.Primed, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
        Assert.False(snapshot.ShowHeroSuggestions);
        Assert.True(snapshot.ShowPreflightSuggestions);
    }

    [Fact]
    public void Reduce_PopupOpened_ForcesPopupEngagedStage()
    {
        // Arrange
        var state = StartComposerState.Default;

        // Act
        var next = StartComposerReducer.Reduce(state, new PopupOpened());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.PopupEngaged, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
    }

    [Fact]
    public void Reduce_PopupClosed_WithDraft_ProjectsExpandedIdle()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new FocusEntered());
        state = StartComposerReducer.Reduce(state, new DraftChanged(true));
        state = StartComposerReducer.Reduce(state, new PopupOpened());
        state = StartComposerReducer.Reduce(state, new FocusExited());

        // Act
        var next = StartComposerReducer.Reduce(state, new PopupClosed());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.ExpandedIdle, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
        Assert.False(snapshot.ShowHeroSuggestions);
        Assert.True(snapshot.ShowPreflightSuggestions);
    }

    [Fact]
    public void Reduce_OutsidePointerPressed_WithDraft_LeavesExpandedIdle()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new Activated());
        state = StartComposerReducer.Reduce(state, new DraftChanged(true));

        // Act
        var next = StartComposerReducer.Reduce(state, new OutsidePointerPressed());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.ExpandedIdle, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
    }

    [Fact]
    public void Reduce_OutsidePointerPressed_WithoutDraft_Collapses()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new Activated());

        // Act
        var next = StartComposerReducer.Reduce(state, new OutsidePointerPressed());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.Collapsed, snapshot.Stage);
        Assert.False(snapshot.IsExpanded);
        Assert.True(snapshot.ShowHeroSuggestions);
    }

    [Fact]
    public void Reduce_SubmitStarted_ProjectsSubmittingStage()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new DraftChanged(true));

        // Act
        var next = StartComposerReducer.Reduce(state, new SubmitStarted());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(StartComposerStage.Submitting, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
        Assert.True(snapshot.FreezeComposerInteractions);
    }

    [Fact]
    public void Reduce_Unloaded_ClearsTransientFacts()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new FocusEntered());
        state = StartComposerReducer.Reduce(state, new PopupOpened());
        state = StartComposerReducer.Reduce(state, new DraftChanged(true));
        state = StartComposerReducer.Reduce(state, new SubmitStarted());

        // Act
        var next = StartComposerReducer.Reduce(state, new Unloaded());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.False(next.HasFocusWithin);
        Assert.False(next.IsPopupOpen);
        Assert.False(next.IsSubmitting);
        Assert.True(next.HasDraft);
        Assert.Equal(StartComposerStage.ExpandedIdle, snapshot.Stage);
    }

    [Fact]
    public void Reduce_SubmitCompleted_AfterUnloaded_DoesNotReactivateComposer()
    {
        // Arrange
        var state = StartComposerState.Default;
        state = StartComposerReducer.Reduce(state, new DraftChanged(true));
        state = StartComposerReducer.Reduce(state, new SubmitStarted());
        state = StartComposerReducer.Reduce(state, new Unloaded());

        // Act
        var next = StartComposerReducer.Reduce(state, new SubmitCompleted());
        var snapshot = StartComposerPolicy.Compute(next);

        // Assert
        Assert.Equal(state, next);
        Assert.Equal(StartComposerStage.ExpandedIdle, snapshot.Stage);
        Assert.True(snapshot.IsExpanded);
    }
}

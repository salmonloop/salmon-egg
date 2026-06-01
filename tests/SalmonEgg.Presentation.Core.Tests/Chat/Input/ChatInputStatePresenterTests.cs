using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Input;

[Collection("NonParallel")]
public sealed class ChatInputStatePresenterTests
{
    private readonly ChatInputStatePresenter _sut = new();

    [Fact]
    public void Present_WithReadySession_ProjectsEnabledComposer()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.True(state.IsInteractiveSurfaceEnabled);
        Assert.True(state.CanSendPrompt);
        Assert.True(state.CanStartVoiceInput);
        Assert.False(state.ShowCancelButton);
        Assert.False(state.ShowVoiceStopButton);
        Assert.False(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WhenPromptSubmitInFlight_DisablesComposerSurfaceWhileKeepingCancelAvailable()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: true,
            IsPromptSubmitInFlight: true,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.PromptInFlight, state.Mode);
        Assert.False(state.IsTextInputEnabled);
        Assert.False(state.AreComposerToolsEnabled);
        Assert.False(state.IsInteractiveSurfaceEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
        Assert.True(state.ShowCancelButton);
        Assert.True(state.CanCancelPrompt);
        Assert.False(state.ShowVoiceStopButton);
        Assert.False(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WhenPromptInFlightAfterSubmit_RestoresComposerSurfaceButKeepsCancelAvailable()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: true,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.True(state.IsInteractiveSurfaceEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.True(state.ShowCancelButton);
        Assert.True(state.CanCancelPrompt);
    }

    [Fact]
    public void Present_WhenVoiceListening_KeepsTextEditingAvailableButRestrictsConflictingActions()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: true,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.VoiceListening, state.Mode);
        Assert.True(state.IsTextInputEnabled);
        Assert.False(state.AreComposerToolsEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
        Assert.False(state.ShowCancelButton);
        Assert.True(state.ShowVoiceStopButton);
        Assert.True(state.CanStopVoiceInput);
        Assert.True(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WithReadySession_ProjectsEnabledComposerWithAuthoritativeInputsOnly()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.True(state.IsInteractiveSurfaceEnabled);
        Assert.True(state.CanSendPrompt);
        Assert.True(state.CanStartVoiceInput);
    }

    [Fact]
    public void Present_WithPendingAskUser_DisablesComposerUntilQuestionResolves()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: true,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.False(state.IsTextInputEnabled);
        Assert.False(state.AreComposerToolsEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
    }

    [Fact]
    public void Present_WhenVoiceListening_StopEscapeActionDoesNotDependOnCapabilityFlagDrift()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: true,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: false));

        Assert.Equal(ChatComposerMode.VoiceListening, state.Mode);
        Assert.True(state.ShowVoiceStopButton);
        Assert.True(state.CanStopVoiceInput);
    }

    [Fact]
    public void Present_WhenVoiceListeningAndBusy_HidesDirectStopAffordanceDuringTransition()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: true,
            VoiceInputTransportState: VoiceInputTransportState.Stopping,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.VoiceListening, state.Mode);
        Assert.False(state.ShowVoiceStopButton);
        Assert.False(state.CanStopVoiceInput);
        Assert.False(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WhenBusy_DisablesComposerSurfaceAndVoiceRestart()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: true,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.False(state.IsTextInputEnabled);
        Assert.False(state.AreComposerToolsEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
    }

    [Fact]
    public void Present_WhenOverlayPresenterVisible_DisablesComposerSurfaceWithoutChangingMode()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Idle,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: true,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.False(state.IsTextInputEnabled);
        Assert.False(state.AreComposerToolsEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
    }

    [Fact]
    public void Present_WhenVoiceTransportBusyButNotListening_KeepsTextEnabled_BlocksVoiceRestart()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsPromptSubmitInFlight: false,
            IsVoiceInputListening: false,
            VoiceInputTransportState: VoiceInputTransportState.Authorizing,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.Enabled, state.Mode);
        Assert.True(state.IsTextInputEnabled);
        Assert.True(state.AreComposerToolsEnabled);
        Assert.True(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
    }
}

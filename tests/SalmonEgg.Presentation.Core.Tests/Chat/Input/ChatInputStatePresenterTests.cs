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
            IsVoiceInputListening: false,
            IsVoiceInputBusy: false,
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
        Assert.False(state.ShowPromptInFlightStatus);
        Assert.False(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WhenPromptInFlight_DisablesComposerAndLeavesCancelAsOnlyEscapeAction()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: true,
            IsVoiceInputListening: false,
            IsVoiceInputBusy: false,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.PromptInFlight, state.Mode);
        Assert.False(state.IsInteractiveSurfaceEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
        Assert.True(state.ShowCancelButton);
        Assert.True(state.CanCancelPrompt);
        Assert.False(state.ShowVoiceStopButton);
        Assert.True(state.ShowPromptInFlightStatus);
        Assert.False(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WhenVoiceListening_KeepsTextEditingAvailableButRestrictsConflictingActions()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsVoiceInputListening: true,
            IsVoiceInputBusy: false,
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
        Assert.False(state.ShowPromptInFlightStatus);
        Assert.True(state.ShowVoiceListeningStatus);
    }

    [Fact]
    public void Present_WithPendingAskUser_DoesNotProjectBlockedComposerMode()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsVoiceInputListening: false,
            IsVoiceInputBusy: false,
            HasPendingAskUserRequest: true,
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
    public void Present_WhenVoiceListening_StopEscapeActionDoesNotDependOnCapabilityFlagDrift()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsVoiceInputListening: true,
            IsVoiceInputBusy: false,
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
    public void Present_WhenVoiceListeningAndBusy_StopEscapeActionRemainsAvailable()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsVoiceInputListening: true,
            IsVoiceInputBusy: true,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: false,
            IsSessionActive: true,
            HasChatService: true,
            IsInitialized: true,
            HasCurrentSessionId: true,
            HasPromptText: true,
            IsVoiceInputSupported: true));

        Assert.Equal(ChatComposerMode.VoiceListening, state.Mode);
        Assert.True(state.ShowVoiceStopButton);
        Assert.True(state.CanStopVoiceInput);
    }

    [Fact]
    public void Present_WhenOnlyLoadingOverlayPresenterIsVisible_DoesNotDisableComposer()
    {
        var state = _sut.Present(new ChatInputStateInput(
            IsBusy: false,
            IsPromptInFlight: false,
            IsVoiceInputListening: false,
            IsVoiceInputBusy: false,
            HasPendingAskUserRequest: false,
            ShouldShowLoadingOverlayPresenter: true,
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
        Assert.True(state.CanStartVoiceInput);
    }
}

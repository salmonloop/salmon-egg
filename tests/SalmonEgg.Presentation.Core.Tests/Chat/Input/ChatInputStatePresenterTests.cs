using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Input;

[Collection("NonParallel")]
public sealed class ChatInputStatePresenterTests
{
    private readonly ChatInputStatePresenter _sut = new();

    [Fact]
    public void Present_WithReadySession_AllowsPromptAndVoiceStart()
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

        Assert.True(state.IsInputEnabled);
        Assert.True(state.CanSendPrompt);
        Assert.True(state.CanStartVoiceInput);
        Assert.False(state.CanStopVoiceInput);
        Assert.True(state.ShowVoiceInputStartButton);
        Assert.False(state.ShowVoiceInputStopButton);
    }

    [Fact]
    public void Present_WithPendingAskUser_DisablesPromptAndVoiceStart()
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

        Assert.False(state.IsInputEnabled);
        Assert.False(state.CanSendPrompt);
        Assert.False(state.CanStartVoiceInput);
    }

    [Fact]
    public void Present_WhileListening_ShowsStopButton()
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

        Assert.False(state.IsInputEnabled);
        Assert.False(state.CanStartVoiceInput);
        Assert.True(state.CanStopVoiceInput);
        Assert.False(state.ShowVoiceInputStartButton);
        Assert.True(state.ShowVoiceInputStopButton);
    }
}

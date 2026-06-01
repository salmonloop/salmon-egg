using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Input;

[Collection("NonParallel")]
public sealed class VoiceInputUiStatePresenterTests
{
    private readonly VoiceInputUiStatePresenter _sut = new();

    [Fact]
    public void Present_WhenVoiceInputIsUnsupported_HidesVoiceAffordance()
    {
        var state = _sut.Present(new VoiceInputUiStateInput(
            IsVoiceInputSupported: false,
            IsVoiceInputListening: false,
            TransportState: VoiceInputTransportState.Idle,
            CanStartVoiceInput: false,
            CanStopVoiceInput: false));

        Assert.Equal(VoiceInputUiMode.Hidden, state.Mode);
        Assert.False(state.ShowStartButton);
        Assert.False(state.ShowStopButton);
        Assert.False(state.ShowProgressRing);
    }

    [Fact]
    public void Present_WhenVoiceInputIsReady_ShowsStartButton()
    {
        var state = _sut.Present(new VoiceInputUiStateInput(
            IsVoiceInputSupported: true,
            IsVoiceInputListening: false,
            TransportState: VoiceInputTransportState.Idle,
            CanStartVoiceInput: true,
            CanStopVoiceInput: false));

        Assert.Equal(VoiceInputUiMode.Ready, state.Mode);
        Assert.True(state.ShowStartButton);
        Assert.False(state.ShowStopButton);
        Assert.False(state.ShowProgressRing);
    }

    [Theory]
    [InlineData(VoiceInputTransportState.Authorizing)]
    [InlineData(VoiceInputTransportState.Starting)]
    [InlineData(VoiceInputTransportState.Stopping)]
    public void Present_WhenVoiceTransportIsBusy_ShowsProgressRing(VoiceInputTransportState transportState)
    {
        var state = _sut.Present(new VoiceInputUiStateInput(
            IsVoiceInputSupported: true,
            IsVoiceInputListening: transportState == VoiceInputTransportState.Stopping,
            TransportState: transportState,
            CanStartVoiceInput: false,
            CanStopVoiceInput: false));

        Assert.Equal(VoiceInputUiMode.Transitioning, state.Mode);
        Assert.False(state.ShowStartButton);
        Assert.False(state.ShowStopButton);
        Assert.True(state.ShowProgressRing);
    }

    [Fact]
    public void Present_WhenVoiceInputIsListening_ShowsStopAffordance()
    {
        var state = _sut.Present(new VoiceInputUiStateInput(
            IsVoiceInputSupported: true,
            IsVoiceInputListening: true,
            TransportState: VoiceInputTransportState.Idle,
            CanStartVoiceInput: false,
            CanStopVoiceInput: true));

        Assert.Equal(VoiceInputUiMode.Listening, state.Mode);
        Assert.False(state.ShowStartButton);
        Assert.True(state.ShowStopButton);
        Assert.False(state.ShowProgressRing);
        Assert.True(state.ShowListeningStatus);
    }
}

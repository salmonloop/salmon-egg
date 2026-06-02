using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class VoiceInputRecognitionCompletionPolicyTests
{
    [Fact]
    public void ShouldTreatAsGracefulEnd_ReturnsTrue_WhenStopWasRequested()
    {
        Assert.True(VoiceInputRecognitionCompletionPolicy.ShouldTreatAsGracefulEnd(
            completionStatus: "AudioQualityFailure",
            stopRequested: true,
            partialResultCount: 0,
            finalResultCount: 0));
    }

    [Fact]
    public void ShouldTreatAsGracefulEnd_ReturnsTrue_WhenRecognitionCompletedSuccessfully()
    {
        Assert.True(VoiceInputRecognitionCompletionPolicy.ShouldTreatAsGracefulEnd(
            completionStatus: "Success",
            stopRequested: false,
            partialResultCount: 0,
            finalResultCount: 0));
    }

    [Fact]
    public void ShouldTreatAsGracefulEnd_ReturnsTrue_WhenUserCanceledAfterRecognitionResultsWereObserved()
    {
        Assert.True(VoiceInputRecognitionCompletionPolicy.ShouldTreatAsGracefulEnd(
            completionStatus: "UserCanceled",
            stopRequested: false,
            partialResultCount: 2,
            finalResultCount: 0));
    }

    [Fact]
    public void ShouldTreatAsGracefulEnd_ReturnsFalse_WhenUserCanceledBeforeAnyRecognitionResult()
    {
        Assert.False(VoiceInputRecognitionCompletionPolicy.ShouldTreatAsGracefulEnd(
            completionStatus: "UserCanceled",
            stopRequested: false,
            partialResultCount: 0,
            finalResultCount: 0));
    }

    [Fact]
    public void BuildFailureMessage_ReturnsReadableFallback_ForUserCanceled()
    {
        Assert.Equal(
            "Voice input was interrupted before any recognition result was produced.",
            VoiceInputRecognitionCompletionPolicy.BuildFailureMessage("UserCanceled"));
    }
}

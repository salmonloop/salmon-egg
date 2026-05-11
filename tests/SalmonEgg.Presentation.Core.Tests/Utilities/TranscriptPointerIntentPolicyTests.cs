using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptPointerIntentPolicyTests
{
    [Fact]
    public void ShouldTrackViewportIntent_WhenPointerStartsOnTranscriptSurface_ReturnsTrue()
    {
        var shouldTrack = TranscriptPointerIntentPolicy.ShouldTrackViewportIntent(
            TranscriptPointerSourceKind.TranscriptSurface);

        Assert.True(shouldTrack);
    }

    [Fact]
    public void ShouldTrackViewportIntent_WhenPointerStartsOnInteractiveChild_ReturnsFalse()
    {
        var shouldTrack = TranscriptPointerIntentPolicy.ShouldTrackViewportIntent(
            TranscriptPointerSourceKind.InteractiveChild);

        Assert.False(shouldTrack);
    }

    [Fact]
    public void ShouldTrackViewportIntent_WhenPointerStartsOnSelectableText_ReturnsFalse()
    {
        var shouldTrack = TranscriptPointerIntentPolicy.ShouldTrackViewportIntent(
            TranscriptPointerSourceKind.SelectableText);

        Assert.False(shouldTrack);
    }
}

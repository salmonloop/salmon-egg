namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Classifies pointer sources before transcript viewport intent handling.
/// </summary>
public static class TranscriptPointerIntentPolicy
{
    /// <summary>
    /// Resolves whether a pointer source should be treated as direct transcript viewport intent.
    /// </summary>
    /// <param name="sourceKind">The semantic source of the pointer event.</param>
    /// <returns><see langword="true" /> when the transcript viewport should handle the pointer intent.</returns>
    public static bool ShouldTrackViewportIntent(TranscriptPointerSourceKind sourceKind)
        => sourceKind == TranscriptPointerSourceKind.TranscriptSurface;
}

/// <summary>
/// Describes the semantic origin of a pointer event inside the transcript surface.
/// </summary>
public enum TranscriptPointerSourceKind
{
    /// <summary>
    /// The event originated from non-interactive transcript surface content.
    /// </summary>
    TranscriptSurface = 0,

    /// <summary>
    /// The event originated from a child control that owns activation or editing.
    /// </summary>
    InteractiveChild = 1,

    /// <summary>
    /// The event originated from selectable text content.
    /// </summary>
    SelectableText = 2,
}

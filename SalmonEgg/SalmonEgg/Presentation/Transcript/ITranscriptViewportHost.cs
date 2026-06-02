namespace SalmonEgg.Presentation.Transcript;

using Microsoft.UI.Xaml;

public enum TranscriptItemScrollAlignment
{
    Default = 0,
    Leading = 1,
}

public interface ITranscriptViewportHost : IDisposable
{
    event EventHandler? ViewportChanged;

    bool HasRealizedItem(int index);

    bool TryGetFirstVisibleIndex(int itemCount, out int index);

    void ScrollItemIntoView(int index, TranscriptItemScrollAlignment alignment = TranscriptItemScrollAlignment.Default);

    bool TryFocusItem(int index, FocusState focusState);

    bool TryScrollByItems(int itemDelta);

    bool TryScrollByPages(int pageDelta);

    bool TryFocusViewport(FocusState focusState);

    bool IsAtBottom(int itemCount, double bottomThreshold, double bottomGeometryTolerance);

    bool IsLastItemVisiblyAtBottom(int itemCount, double bottomThreshold, double bottomGeometryTolerance);
}

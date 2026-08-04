using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Decides the order the navigation pane applies to a project's bound session children.
/// </summary>
/// <remarks>
/// <para>
/// Recency ordering must not relocate rows that are already rendered while the pane is busy.
/// WinUI documents the failure directly: an item "that is currently selected and/or has focus
/// where the move is achieved by a 'Remove' followed by an 'Add' will lose focus and no longer be
/// selected", and prescribes <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>
/// so a real Move action is delivered instead. Uno 6.6.x decomposes
/// <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Move"/> back into
/// Remove+Add inside <c>ItemsRepeater</c>, so every reorder recycles a realized container. Because
/// the recycle pool does not clear the selected flag and NavigationView's deselect of the previous
/// item silently no-ops once it can no longer resolve that container, a single poisoned container
/// keeps being handed to other rows and repainted selected. The observable result is several
/// session rows carrying the selected mask at once, contradicting NavigationView's own contract
/// that "the entire NavigationView will show no more than one selection indicator".
/// </para>
/// <para>
/// Insertions and removals are safe: for those actions the repeater only re-indexes the surviving
/// realized elements (it recycles just the element whose own data was removed). So the invariant
/// that keeps the defect unreachable is simply "do not move rows that are already rendered while
/// a selection is in flight" — appending newcomers is fine.
/// </para>
/// <para>
/// That hold applies only while the pane is unsettled (an activation in flight, or the catalog
/// still loading). Once quiet, the pane converges to the full recency order on the next rebuild,
/// so ordering stays correct and the hold cannot starve.
/// </para>
/// </remarks>
public static class NavigationSessionOrderPolicy
{
    /// <summary>
    /// Projects <paramref name="desiredOrder"/> onto the order the pane should actually apply.
    /// </summary>
    /// <param name="desiredOrder">Sessions in the freshly computed (recency) order.</param>
    /// <param name="renderedOrder">
    /// Session identifiers currently rendered for the project, in render order.
    /// </param>
    /// <param name="preserveRenderedOrder">
    /// <see langword="true"/> while the pane is unsettled, which holds every already-rendered row
    /// at its position and appends newcomers; <see langword="false"/> to apply recency verbatim.
    /// </param>
    /// <param name="identify">Projects a session entry onto its conversation identifier.</param>
    public static List<T> ResolveAppliedOrder<T>(
        IReadOnlyList<T> desiredOrder,
        IReadOnlyList<string> renderedOrder,
        bool preserveRenderedOrder,
        Func<T, string> identify)
    {
        ArgumentNullException.ThrowIfNull(desiredOrder);
        ArgumentNullException.ThrowIfNull(renderedOrder);
        ArgumentNullException.ThrowIfNull(identify);

        if (!preserveRenderedOrder || desiredOrder.Count == 0 || renderedOrder.Count == 0)
        {
            return desiredOrder.ToList();
        }

        var desiredById = new Dictionary<string, T>(desiredOrder.Count, StringComparer.Ordinal);
        foreach (var entry in desiredOrder)
        {
            desiredById.TryAdd(identify(entry), entry);
        }

        // Already-rendered rows keep their exact positions, so no realized container is moved.
        var applied = new List<T>(desiredOrder.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var renderedId in renderedOrder)
        {
            if (desiredById.TryGetValue(renderedId, out var entry) && placed.Add(renderedId))
            {
                applied.Add(entry);
            }
        }

        // Newcomers follow in recency order. An insert only re-indexes the rows after it; it does
        // not recycle them, so this cannot strand a selection visual.
        foreach (var entry in desiredOrder)
        {
            if (placed.Add(identify(entry)))
            {
                applied.Add(entry);
            }
        }

        return applied;
    }
}

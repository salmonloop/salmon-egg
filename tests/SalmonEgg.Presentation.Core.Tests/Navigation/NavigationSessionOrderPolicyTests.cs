using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.Core.Services;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class NavigationSessionOrderPolicyTests
{
    [Fact]
    public void ResolveAppliedOrder_WhenSettled_AppliesRecencyOrderVerbatim()
    {
        var applied = Resolve(
            desiredOrder: ["s3", "s2", "s1"],
            renderedOrder: ["s1", "s2", "s3"],
            preserveRenderedOrder: false);

        Assert.Equal(["s3", "s2", "s1"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenUnsettled_HoldsEveryRenderedRowInPlace()
    {
        // Recency wants a full reversal. Moving a rendered row recycles its container, which is
        // what strands the selected mask, so the rendered order must survive untouched.
        var applied = Resolve(
            desiredOrder: ["s3", "s2", "s1"],
            renderedOrder: ["s1", "s2", "s3"],
            preserveRenderedOrder: true);

        Assert.Equal(["s1", "s2", "s3"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenUnsettled_AppendsNewcomersAfterRenderedRows()
    {
        // Inserts only re-index the rows after them; they do not recycle containers.
        var applied = Resolve(
            desiredOrder: ["s9", "s8", "s2", "s1"],
            renderedOrder: ["s1", "s2"],
            preserveRenderedOrder: true);

        Assert.Equal(["s1", "s2", "s9", "s8"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenUnsettled_DropsRowsThatLeftTheDesiredSet()
    {
        // s2 was archived or reassigned: it is no longer part of this project's desired set, so it
        // is removed rather than held. Removing its own row does not disturb the others.
        var applied = Resolve(
            desiredOrder: ["s3", "s1"],
            renderedOrder: ["s1", "s2", "s3"],
            preserveRenderedOrder: true);

        Assert.Equal(["s1", "s3"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenNothingRendered_AppliesRecencyOrder()
    {
        var applied = Resolve(
            desiredOrder: ["s3", "s2", "s1"],
            renderedOrder: [],
            preserveRenderedOrder: true);

        Assert.Equal(["s3", "s2", "s1"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenDesiredSetIsEmpty_ReturnsEmpty()
    {
        var applied = Resolve(
            desiredOrder: [],
            renderedOrder: ["s1", "s2"],
            preserveRenderedOrder: true);

        Assert.Empty(applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenUnsettled_StaysAFaithfulPermutationOfTheDesiredSet()
    {
        var desired = new[] { "s5", "s4", "s3", "s2", "s1" };

        var applied = Resolve(
            desiredOrder: desired,
            renderedOrder: ["s2", "s4", "s1"],
            preserveRenderedOrder: true);

        Assert.Equal(desired.Length, applied.Count);
        Assert.Equal(desired.OrderBy(id => id), applied.OrderBy(id => id));
        // Rendered rows first, in rendered order; newcomers after, in recency order.
        Assert.Equal(["s2", "s4", "s1", "s5", "s3"], applied);
    }

    [Fact]
    public void ResolveAppliedOrder_WhenRenderedOrderHasDuplicates_PlacesEachRowOnce()
    {
        var applied = Resolve(
            desiredOrder: ["s2", "s1"],
            renderedOrder: ["s1", "s1", "s2"],
            preserveRenderedOrder: true);

        Assert.Equal(["s1", "s2"], applied);
    }

    private static List<string> Resolve(
        IReadOnlyList<string> desiredOrder,
        IReadOnlyList<string> renderedOrder,
        bool preserveRenderedOrder)
        => NavigationSessionOrderPolicy.ResolveAppliedOrder(
            desiredOrder,
            renderedOrder,
            preserveRenderedOrder,
            static id => id);
}

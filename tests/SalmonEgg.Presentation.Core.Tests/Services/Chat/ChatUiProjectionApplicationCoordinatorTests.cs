using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class ChatUiProjectionApplicationCoordinatorTests
{
    private readonly ChatStateProjector _projector = new();
    private readonly ChatUiProjectionApplicationCoordinator _coordinator = new();

    [Fact]
    public void ShouldApply_WhenProjectionHasBlankHydratedId_ReturnsTrue()
    {
        // Projections computed before hydration complete are always applied; the dedup
        // only governs hydrated projections for an armed activation.
        var projection = Projection(hydratedId: null);

        Assert.True(_coordinator.ShouldApply(projection, activationVersion: 1L));
    }

    [Fact]
    public void ShouldApply_WhenNotArmed_ReturnsTrue()
    {
        var projection = Projection(hydratedId: "conv-1");

        Assert.True(_coordinator.ShouldApply(projection, activationVersion: 1L));
    }

    [Fact]
    public void ShouldApply_FirstHydratedProjectionForArmedActivation_AppliesAndRemembers()
    {
        _coordinator.ArmActivationSelectionProjection("conv-1", activationVersion: 1L);

        Assert.True(_coordinator.ShouldApply(Projection("conv-1"), 1L));
    }

    [Fact]
    public void ShouldApply_SecondProjectionIdenticalToFirst_Suppresses()
    {
        _coordinator.ArmActivationSelectionProjection("conv-1", activationVersion: 1L);
        _coordinator.ShouldApply(Projection("conv-1"), 1L);

        // The exact same hydrated projection arriving again must be suppressed so the
        // activation replay does not redundantly re-apply the UI state.
        Assert.False(_coordinator.ShouldApply(Projection("conv-1"), 1L));
    }

    [Fact]
    public void ShouldApply_SecondProjectionDifferentFromFirst_AppliesAndDisarms()
    {
        _coordinator.ArmActivationSelectionProjection("conv-1", activationVersion: 1L);
        _coordinator.ShouldApply(Projection("conv-1", isHydrating: false), 1L);

        // A genuinely different projection (here IsHydrating flipped) is applied, and the
        // coordinator disarms so a third projection is no longer deduped against the first.
        Assert.True(_coordinator.ShouldApply(Projection("conv-1", isHydrating: true), 1L));
        Assert.True(_coordinator.ShouldApply(Projection("conv-1", isHydrating: true), 1L));
    }

    [Fact]
    public void ShouldApply_NewerActivationVersion_DisarmsAndApplies()
    {
        _coordinator.ArmActivationSelectionProjection("conv-1", activationVersion: 1L);

        // A newer activation supersedes the armed one.
        Assert.True(_coordinator.ShouldApply(Projection("conv-1"), activationVersion: 2L));
        // The arm is cleared, so a stale-version projection now flows through.
        Assert.True(_coordinator.ShouldApply(Projection("conv-1"), activationVersion: 1L));
    }

    [Fact]
    public void ShouldApply_DifferentConversationIdThanArmed_AppliesWithoutDisarming()
    {
        _coordinator.ArmActivationSelectionProjection("conv-1", activationVersion: 1L);

        // A projection for a different conversation is not this activation's replay.
        Assert.True(_coordinator.ShouldApply(Projection("conv-2"), activationVersion: 1L));
        // The arm survives, so the matching projection is still deduped as the first.
        Assert.True(_coordinator.ShouldApply(Projection("conv-1"), activationVersion: 1L));
    }

    [Fact]
    public void ArmActivationSelectionProjection_BlankConversationId_IsNoOp()
    {
        _coordinator.ArmActivationSelectionProjection("   ", activationVersion: 1L);

        // No arm was recorded, so every projection flows through.
        Assert.True(_coordinator.ShouldApply(Projection("conv-1"), activationVersion: 1L));
    }

    [Fact]
    public void ShouldApply_NullProjection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _coordinator.ShouldApply(null!, activationVersion: 1L));
    }

    private ChatUiProjection Projection(string? hydratedId, bool isHydrating = false)
        => _projector.Apply(
            isHydrating ? ChatState.Empty with { IsHydrating = true } : ChatState.Empty,
            ChatConnectionState.Empty,
            hydratedId,
            binding: null);
}

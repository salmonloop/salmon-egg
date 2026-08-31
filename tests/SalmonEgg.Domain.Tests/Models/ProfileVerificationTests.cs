using System;
using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Domain.Tests.Models;

/// <summary>
/// Pins the three-state verification contract that the connection list's warning affordance and the
/// setup wizard's save step both read.
/// </summary>
/// <remarks>
/// The distinction under test is <see cref="ProfileVerificationState.Unknown"/> versus
/// <see cref="ProfileVerificationState.Unverified"/>. Collapsing the two — the shape a boolean would
/// force — puts a "not verified" mark on every profile written before this state existed and on every
/// profile the CLI or the profile editor creates, none of which were ever offered a test.
/// </remarks>
public sealed class ProfileVerificationTests
{
    [Fact]
    public void FreshProfile_IsUnknown_SoNothingIsRetroactivelyFlagged()
    {
        var profile = new ServerConfiguration();

        Assert.Equal(ProfileVerificationState.Unknown, profile.Verification.State);
        // Both false: an unasked question is neither a pass nor a refusal, and the warning
        // affordance binds to IsUnverified.
        Assert.False(profile.Verification.IsVerified);
        Assert.False(profile.Verification.IsUnverified);
        Assert.Null(profile.Verification.VerifiedAtUtc);
    }

    /// <summary>
    /// <c>default</c> has to be <see cref="ProfileVerification.Unknown"/> on its own, not only because
    /// <see cref="ServerConfiguration"/> happens to initialize the property. Any writer that builds a
    /// configuration without mentioning verification — and every writer outside the wizard does — gets
    /// this value.
    /// </summary>
    [Fact]
    public void Default_IsUnknown_WithoutAnyInitializer()
    {
        Assert.Equal(ProfileVerification.Unknown, default(ProfileVerification));
        Assert.Equal(ProfileVerificationState.Unknown, default(ProfileVerification).State);
    }

    [Fact]
    public void Unverified_CarriesNoTimestamp_SoNoStaleEvidenceSurvivesTheVerdict()
    {
        var verification = ProfileVerification.Unverified;

        Assert.Equal(ProfileVerificationState.Unverified, verification.State);
        Assert.True(verification.IsUnverified);
        Assert.False(verification.IsVerified);
        Assert.Null(verification.VerifiedAtUtc);
    }

    [Fact]
    public void Verified_CarriesTheMomentItPassed()
    {
        var passedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        var verification = ProfileVerification.Verified(passedAt);

        Assert.Equal(ProfileVerificationState.Verified, verification.State);
        Assert.True(verification.IsVerified);
        Assert.False(verification.IsUnverified);
        Assert.Equal(passedAt, verification.VerifiedAtUtc);
    }

    /// <summary>
    /// The timestamp is normalized to UTC on the way in, so two profiles verified at the same instant in
    /// different time zones compare equal and persist identically.
    /// </summary>
    [Fact]
    public void Verified_NormalizesTheTimestampToUtc()
    {
        var localised = new DateTimeOffset(2026, 8, 30, 20, 0, 0, TimeSpan.FromHours(8));

        var verification = ProfileVerification.Verified(localised);

        Assert.Equal(TimeSpan.Zero, verification.VerifiedAtUtc!.Value.Offset);
        Assert.Equal(localised.UtcDateTime, verification.VerifiedAtUtc!.Value.UtcDateTime);
        Assert.Equal(
            ProfileVerification.Verified(localised.ToOffset(TimeSpan.FromHours(-5))),
            verification);
    }
}

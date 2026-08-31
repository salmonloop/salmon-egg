using System;

namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// Whether a connection profile's launch configuration has been proven to start and speak ACP.
    /// </summary>
    /// <remarks>
    /// Three states rather than a boolean, for the same reason
    /// <see cref="AcpSetup.AcpComponentAvailability"/> separates <c>Missing</c> from
    /// <c>Undetermined</c>: never having asked is not the same answer as having asked and been told no.
    ///
    /// <see cref="Unknown"/> is first so it is <c>default</c>. Profiles written before this state
    /// existed, and profiles created by the CLI or the profile editor — neither of which runs a
    /// connectivity test — land here, and a profile that was never offered a test must not be shown as
    /// one the user declined to test.
    /// </remarks>
    public enum ProfileVerificationState
    {
        /// <summary>No verdict was ever recorded for this profile.</summary>
        Unknown,

        /// <summary>The profile was offered a connectivity test and saved without passing one.</summary>
        Unverified,

        /// <summary>The profile passed an end-to-end connectivity test before it was saved.</summary>
        Verified
    }

    /// <summary>
    /// A profile's verification verdict together with when it was reached.
    /// </summary>
    /// <remarks>
    /// One value rather than a state field beside a nullable timestamp, because those two can disagree:
    /// a stale timestamp left behind by an earlier pass would outlive the verdict it belonged to, and a
    /// <see cref="ProfileVerificationState.Verified"/> profile with no timestamp cannot say how old its
    /// evidence is. The factories below are the only way to build one, so neither combination is
    /// representable.
    ///
    /// <c>default</c> is <see cref="Unknown"/>, so a profile constructed without mentioning verification
    /// — every CLI and profile-editor profile — is correct without the constructor having to know this
    /// type exists.
    /// </remarks>
    public readonly record struct ProfileVerification
    {
        private ProfileVerification(ProfileVerificationState state, DateTimeOffset? verifiedAtUtc)
        {
            State = state;
            VerifiedAtUtc = verifiedAtUtc;
        }

        public ProfileVerificationState State { get; }

        /// <summary>
        /// When the passing test ran, in UTC. Non-null exactly when
        /// <see cref="State"/> is <see cref="ProfileVerificationState.Verified"/>.
        /// </summary>
        public DateTimeOffset? VerifiedAtUtc { get; }

        /// <summary>No verdict was ever recorded. Also the value of <c>default</c>.</summary>
        public static ProfileVerification Unknown => default;

        /// <summary>The user was offered a test and saved the profile without passing one.</summary>
        public static ProfileVerification Unverified
            => new(ProfileVerificationState.Unverified, verifiedAtUtc: null);

        /// <summary>A test passed at <paramref name="verifiedAtUtc"/>.</summary>
        public static ProfileVerification Verified(DateTimeOffset verifiedAtUtc)
            => new(ProfileVerificationState.Verified, verifiedAtUtc.ToUniversalTime());

        public bool IsVerified => State == ProfileVerificationState.Verified;

        /// <summary>
        /// True only for a profile that was offered a test and saved anyway.
        /// </summary>
        /// <remarks>
        /// Deliberately false for <see cref="Unknown"/>. This is what a warning affordance binds to, and
        /// an <see cref="Unknown"/> profile has no evidence either way — warning about it would put a
        /// mark on every profile that predates this state.
        /// </remarks>
        public bool IsUnverified => State == ProfileVerificationState.Unverified;
    }
}

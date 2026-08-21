namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Supplies the store used for a mutation's rollback material.
/// </summary>
/// <remarks>
/// Rollback material is a short-lived copy of a secret that is already stored somewhere, written so an
/// interrupted mutation can be undone and deleted once the mutation commits. It is not new credential
/// material the user is introducing, so it is not what
/// <see cref="SecureStorageDowngradePolicy.FailClosed"/> exists to prevent.
///
/// This distinction matters because the copy has to succeed for a delete to work at all: refusing it
/// would leave an operator unable to clear a secret that an earlier session had already downgraded — the
/// opposite of what a fail-closed policy is for.
///
/// The copy cannot newly expose anything. A downgrade only happens when the platform store is
/// unavailable, and in that case the value being copied was itself read back from the weaker store, so it
/// is already unprotected. When the platform store works, the copy lands there and no downgrade occurs.
///
/// Implemented only by stores that make a downgrade decision. Stores with no such decision are used
/// directly for both purposes.
/// </remarks>
public interface ISecureStorageRecoveryMaterialSource
{
    /// <summary>
    /// Gets the store to use for rollback material.
    /// </summary>
    ISecureStorage GetRecoveryMaterialStore();
}

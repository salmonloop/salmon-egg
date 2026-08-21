namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Notified once conversation restore has been attempted, so work that needs the conversation catalog
/// can proceed.
/// </summary>
/// <remarks>
/// A push from the startup workflow rather than a pull from it: the deferred work is resolved while
/// the workflow itself is still being constructed, so a dependency in that direction would be a cycle.
/// Called exactly once per restore attempt, including a failed one.
/// </remarks>
public interface IConversationRestoreCompletionSink
{
    void OnConversationRestoreCompleted(bool restored);
}

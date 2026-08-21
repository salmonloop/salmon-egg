namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

/// <summary>
/// Single owner of the question "which project does this conversation activate under".
/// </summary>
/// <remarks>
/// Callers only supply the conversation's own affinity facts. The configured projects, remote
/// directories and navigation membership come from the authoritative preferences, so no caller
/// gets to assemble its own <see cref="ProjectAffinityRequest"/>.
/// </remarks>
public interface IConversationProjectAffinityResolver
{
    string? ResolveActivationProjectId(ConversationProjectAffinityRequest request);
}

public sealed record ConversationProjectAffinityRequest(
    string? Cwd,
    string? BoundProfileId,
    string? RemoteSessionId,
    string? OverrideProjectId);

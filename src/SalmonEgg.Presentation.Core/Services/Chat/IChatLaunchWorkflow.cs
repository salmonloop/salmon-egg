using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record ChatLaunchRequest(
    string PromptText,
    string? ProjectId,
    string? Cwd);

/// <summary>
/// Outcome of a Start-page launch attempt.
/// <see cref="PromptDispatched"/> is the only success that should clear the draft prompt.
/// <see cref="Incomplete"/> covers intentional pauses (connection still in progress or
/// transport configuration required). <see cref="Failed"/> is a user-visible launch fault.
/// </summary>
public enum ChatLaunchCompletion
{
    PromptDispatched = 0,
    Incomplete = 1,
    Failed = 2,
}

/// <summary>
/// Single-owner Start launch workflow: create a local session, activate it through navigation,
/// connect if needed, then dispatch the initial prompt.
/// </summary>
public interface IChatLaunchWorkflow
{
    Task<ChatLaunchCompletion> StartSessionAndSendAsync(
        ChatLaunchRequest request,
        CancellationToken cancellationToken = default);
}

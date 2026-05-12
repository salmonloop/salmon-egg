using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Single-owner Start launch workflow: create a local session, activate it through navigation,
/// connect if needed, then dispatch the initial prompt.
/// </summary>
public interface IChatLaunchWorkflow
{
    Task StartSessionAndSendAsync(
        string promptText,
        string? projectId,
        CancellationToken cancellationToken = default);
}

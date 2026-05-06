using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal sealed record AcpSessionRecoveryProjection(
    SessionLoadResponse SessionLoadResponse,
    string CompletedRuntimeReason)
{
    public static AcpSessionRecoveryProjection FromLoad(SessionLoadResponse response)
        => new(response, ConversationRuntimeReasons.SessionLoadCompleted);

    public static AcpSessionRecoveryProjection FromResume(SessionResumeResponse response)
        => new(
            new SessionLoadResponse(
                response.Modes,
                response.ConfigOptions),
            ConversationRuntimeReasons.SessionResumeCompleted);
}

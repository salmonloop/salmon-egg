using System;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record ChatSessionUpdateRoute(
    bool Handled,
    bool Ignored,
    string? IgnoredReason,
    bool ShouldSetConfigAuthoritative,
    AcpSessionUpdateDelta? Delta)
{
    public static ChatSessionUpdateRoute Unhandled { get; } = new(false, false, null, false, null);

    public static ChatSessionUpdateRoute IgnoredHandled(string reason)
        => new(true, true, reason, false, null);

    public static ChatSessionUpdateRoute Applied(
        AcpSessionUpdateDelta delta,
        bool shouldSetConfigAuthoritative = false)
        => new(true, false, null, shouldSetConfigAuthoritative, delta);
}

public sealed class ChatSessionUpdateRouter
{
    private readonly IAcpSessionUpdateProjector _projector;

    public ChatSessionUpdateRouter(IAcpSessionUpdateProjector projector)
    {
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    public ChatSessionUpdateRoute Route(
        SessionUpdateEventArgs args,
        bool isConversationConfigAuthoritative)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Update switch
        {
            PlanUpdate => ChatSessionUpdateRoute.Applied(_projector.Project(args)),
            CurrentModeUpdate when isConversationConfigAuthoritative
                => ChatSessionUpdateRoute.IgnoredHandled("ConfigOptionsAuthoritative"),
            CurrentModeUpdate => ChatSessionUpdateRoute.Applied(_projector.Project(args)),
            ConfigOptionUpdate optionUpdate => ChatSessionUpdateRoute.Applied(
                _projector.Project(args),
                shouldSetConfigAuthoritative: optionUpdate.ConfigOptions is not null),
            SessionInfoUpdate => ChatSessionUpdateRoute.Applied(_projector.Project(args)),
            UsageUpdate => ChatSessionUpdateRoute.Applied(_projector.Project(args)),
            AvailableCommandsUpdate => ChatSessionUpdateRoute.Applied(_projector.Project(args)),
            _ => ChatSessionUpdateRoute.Unhandled
        };
    }
}

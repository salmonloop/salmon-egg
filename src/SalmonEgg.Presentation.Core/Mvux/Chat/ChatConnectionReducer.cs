using System;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public static class ChatConnectionReducer
{
    public static ChatConnectionState Reduce(ChatConnectionState? state, ChatConnectionAction action)
    {
        var current = state ?? ChatConnectionState.Empty;
        var nextGeneration = checked(current.Generation + 1);

        return action switch
        {
            SetConnectionPhaseAction setPhase => current with
            {
                Phase = setPhase.Phase,
                Error = setPhase.Error,
                ForegroundTransportProfileId = setPhase.Phase switch
                {
                    ConnectionPhase.Disconnected or ConnectionPhase.Error => null,
                    _ => current.ForegroundTransportProfileId
                },
                NewSessionDraft = setPhase.Phase switch
                {
                    ConnectionPhase.Disconnected or ConnectionPhase.Error => null,
                    _ => current.NewSessionDraft
                },
                Generation = nextGeneration
            },
            SetSelectedProfileIntentAction setSettings => current with
            {
                SelectedProfileIntentId = setSettings.ProfileId,
                Generation = nextGeneration
            },
            SetForegroundTransportProfileAction setForeground => current with
            {
                ForegroundTransportProfileId = setForeground.ProfileId,
                NewSessionDraft = string.Equals(current.ForegroundTransportProfileId, setForeground.ProfileId, StringComparison.Ordinal)
                    ? current.NewSessionDraft
                    : null,
                Generation = nextGeneration
            },
            SetConnectionInstanceIdAction setConnectionInstanceId => current with
            {
                ConnectionInstanceId = setConnectionInstanceId.ConnectionInstanceId,
                NewSessionDraft = string.Equals(current.ConnectionInstanceId, setConnectionInstanceId.ConnectionInstanceId, StringComparison.Ordinal)
                    ? current.NewSessionDraft
                    : null,
                Generation = nextGeneration
            },
            SetConnectionAuthenticationStateAction setAuth => current with
            {
                IsAuthenticationRequired = setAuth.IsRequired,
                AuthenticationHintMessage = setAuth.HintMessage,
                Generation = nextGeneration
            },
            SetNewSessionDraftAction setDraft => current with
            {
                NewSessionDraft = setDraft.Draft,
                Generation = nextGeneration
            },
            ClearNewSessionDraftAction => current with
            {
                NewSessionDraft = null,
                Generation = nextGeneration
            },
            ResetConnectionStateAction => ChatConnectionState.Empty with
            {
                SelectedProfileIntentId = current.SelectedProfileIntentId,
                ConnectionInstanceId = current.ConnectionInstanceId,
                Generation = nextGeneration
            },
            _ => current with { Generation = nextGeneration }
        };
    }
}

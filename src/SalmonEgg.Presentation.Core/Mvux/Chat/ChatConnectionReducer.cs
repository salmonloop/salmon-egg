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
                Generation = nextGeneration
            },
            SetSelectedProfileAction setProfile => current with
            {
                SelectedProfileId = setProfile.ProfileId,
                Generation = nextGeneration
            },
            SetConnectionAuthenticationStateAction setAuth => current with
            {
                IsAuthenticationRequired = setAuth.IsRequired,
                AuthenticationHintMessage = setAuth.HintMessage,
                Generation = nextGeneration
            },
            ResetConnectionStateAction => ChatConnectionState.Empty with
            {
                Generation = nextGeneration
            },
            _ => current with { Generation = nextGeneration }
        };
    }
}

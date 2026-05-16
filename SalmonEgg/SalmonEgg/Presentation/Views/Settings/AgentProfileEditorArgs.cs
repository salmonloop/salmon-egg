namespace SalmonEgg.Presentation.Views.Settings;

public sealed class AgentProfileEditorArgs
{
    public AgentProfileEditorArgs(bool isEditing, string? profileId)
    {
        IsEditing = isEditing;
        ProfileId = profileId;
    }

    public bool IsEditing { get; }

    public string? ProfileId { get; }
}

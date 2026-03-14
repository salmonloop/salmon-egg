namespace SalmonEgg.Presentation.Services;

public sealed class NoopFloatingChatWindowService : IFloatingChatWindowService
{
    public bool IsOpen => false;

    public void Toggle()
    {
    }

    public void Show()
    {
    }

    public void Hide()
    {
    }
}

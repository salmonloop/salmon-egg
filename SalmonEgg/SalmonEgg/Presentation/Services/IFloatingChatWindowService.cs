namespace SalmonEgg.Presentation.Services;

public interface IFloatingChatWindowService
{
    bool IsOpen { get; }
    void Toggle();
    void Show();
    void Hide();
}

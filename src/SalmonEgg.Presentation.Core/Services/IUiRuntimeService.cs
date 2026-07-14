namespace SalmonEgg.Presentation.Services;

public interface IUiRuntimeService
{
    void InitializeAnimations();
    void SetAnimationsEnabled(bool enabled);
    void ReloadShell();
}

using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Presentation.Services;

public sealed class UiRuntimeService : IUiRuntimeService
{
    public void SetAnimationsEnabled(bool enabled)
    {
        UiMotionController.Current.IsAnimationEnabled = enabled;
        App.ApplyReducedMotion(!enabled);
    }

    public void ReloadShell()
    {
        App.ReloadMainShell();
    }
}

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Read-only application foreground state projected by the native shell.
/// </summary>
public interface IApplicationVisibilityState
{
    bool IsActive { get; }
}

namespace SalmonEgg.Presentation.Services;

public readonly record struct ShellNavigationResult(bool Succeeded, string? FailureReason = null)
{
    public static ShellNavigationResult Success() => new(true);

    public static ShellNavigationResult Failed(string failureReason) => new(false, failureReason);
}

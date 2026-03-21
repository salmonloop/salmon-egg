namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum BindingUpdateStatus
{
    Success,
    NotFound,
    Archived,
    Error
}

public readonly record struct BindingUpdateResult(
    BindingUpdateStatus Status,
    string? ErrorMessage)
{
    public static BindingUpdateResult Success()
        => new(BindingUpdateStatus.Success, null);

    public static BindingUpdateResult NotFound()
        => new(BindingUpdateStatus.NotFound, null);

    public static BindingUpdateResult Archived()
        => new(BindingUpdateStatus.Archived, null);

    public static BindingUpdateResult Error(string? errorMessage)
        => new(BindingUpdateStatus.Error, string.IsNullOrWhiteSpace(errorMessage) ? "UnknownError" : errorMessage);
}

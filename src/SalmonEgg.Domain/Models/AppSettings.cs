namespace SalmonEgg.Domain.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";

    public bool IsAnimationEnabled { get; set; } = true;

    public string? LastSelectedServerId { get; set; }
}

